using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Curated station → item-group map (server-synced config, section [Stations]),
    /// mirroring <see cref="Groups"/>. Keys are the piece's <c>m_name</c> token; values are
    /// comma-separated group names that must exist in [ItemGroups]. During Organize, a chest
    /// sitting next to a station inherits that station's groups.
    ///
    /// Two kinds of "station" are detected, nearest one wins within Plugin.StationRange:
    ///  - True crafting stations, scanned from <c>CraftingStation.m_allStations</c>. (We do NOT use
    ///    <c>GetCraftingStation</c> — it only matches a ~0.1 m use-trigger, never an adjacent chest.)
    ///  - Processing pieces — smelters, kilns, blast furnaces, eitr refineries, fermenters, cooking
    ///    stations — which are NOT CraftingStations. They register here from Awake/OnDestroyed
    ///    patches (Patches/ProcessorPatches.cs), same lifecycle pattern as ContainerTracker.
    ///
    /// Windmills are neither (no m_name) and stay undetected — use a pin instead.
    /// MODDED stations: the Info-level log prints every detected token so unmapped ones can be
    /// added via the CustomStations entry without a rebuild.</summary>
    internal static class Stations
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "$piece_forge",         "metals, ores" },
            { "$piece_workbench",     "wood, hides" },
            { "$piece_stonecutter",   "stone" },
            { "$piece_cauldron",      "cooking, meat, seeds" },
            { "$piece_fermenter",     "meads" },
            { "$piece_blackforge",    "metals, valuables" },
            { "$piece_magetable",     "valuables, meads" },
            { "$piece_smelter",       "ores, fuel" },
            { "$piece_blastfurnace",  "ores, fuel" },
        };

        private static readonly Dictionary<string, ConfigEntry<string>> Entries =
            new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, List<string>> Parsed =
            new Dictionary<string, List<string>>();
        private static readonly List<string> Empty = new List<string>();
        private static ConfigEntry<string> Extra;   // free-form mappings for modded stations

        // Live processing pieces (Smelter/Fermenter/CookingStation) → their m_name token.
        // Pruned on scan because OnDestroyed only fires on damage-destruction, not area unload.
        private static readonly Dictionary<Component, string> Processors =
            new Dictionary<Component, string>();
        private static readonly List<Component> Dead = new List<Component>();

        // private static List<CraftingStation> m_allStations
        private static readonly FieldInfo AllStationsField =
            AccessTools.Field(typeof(CraftingStation), "m_allStations");

        /// <summary>A hammer placement GHOST instantiates the real piece prefab — its component
        /// Awake (and our patch) fires, but its ZNetView is force-disabled and never gets a ZDO.
        /// Only pieces with a live ZDO are real, placed stations.</summary>
        private static bool IsReal(Component c)
        {
            var nv = c != null ? c.GetComponentInParent<ZNetView>() : null;
            return nv != null && nv.IsValid();
        }

        // 1.1.2: the prune below used to run on EVERY registration, making a zone load O(P^2) in the
        // number of tracked processors. Amortize it instead — the dead keys it collects are cheap to
        // carry for a few more registrations, and GroupsForChest prunes on scan as well.
        private const int PruneEvery = 64;
        private static int _sinceLastPrune;

        internal static void RegisterProcessor(Component c, string mName)
        {
            if (c == null || string.IsNullOrEmpty(mName) || !IsReal(c)) return;   // skip placement ghosts

            // Opportunistic prune: zone unload destroys pieces WITHOUT firing OnDestroyed, and a
            // dedicated server (or a client that never presses Organize) would otherwise accumulate
            // dead keys forever. Registration happens exactly when zones load, so this stays amortized.
            if (++_sinceLastPrune >= PruneEvery)
            {
                _sinceLastPrune = 0;
                foreach (var kv in Processors)
                    if (kv.Key == null) Dead.Add(kv.Key);
                if (Dead.Count > 0)
                {
                    foreach (var d in Dead) Processors.Remove(d);
                    Dead.Clear();
                }
            }
            Processors[c] = mName;
        }

        internal static void UnregisterProcessor(Component c)
        {
            if (c != null) Processors.Remove(c);
        }

        internal static void Init(ConfigFile config)
        {
            foreach (var kv in Defaults)
            {
                Entries[kv.Key] = config.Bind("Stations", kv.Key, kv.Value,
                    new ConfigDescription("Comma-separated group names (see [ItemGroups]) this station attracts during Organize.",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }
            Extra = config.Bind("Stations", "CustomStations", "",
                new ConfigDescription("Extra mappings for modded/unlisted stations. Format: 'token=group1,group2; token2=group3'. " +
                    "Copy a station's token from the log (Info) after pressing Organize, e.g. '$piece_blacksmith=metals,ores'.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            config.SettingChanged += (_, __) => Rebuild();
            Rebuild();
        }

        private static void Rebuild()
        {
            Parsed.Clear();
            foreach (var kv in Entries)
            {
                var groups = new List<string>();
                foreach (var raw in kv.Value.Value.Split(','))
                {
                    var t = raw.Trim().ToLowerInvariant();
                    if (t.Length > 0) groups.Add(t);
                }
                Parsed[kv.Key] = groups;
            }

            // Merge user-supplied modded-station mappings (token case is preserved to match m_name).
            if (Extra != null && !string.IsNullOrEmpty(Extra.Value))
            {
                foreach (var pair in Extra.Value.Split(';'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var token = pair.Substring(0, eq).Trim();
                    if (token.Length == 0) continue;
                    var groups = new List<string>();
                    foreach (var raw in pair.Substring(eq + 1).Split(','))
                    {
                        var t = raw.Trim().ToLowerInvariant();
                        if (t.Length > 0) groups.Add(t);
                    }
                    if (groups.Count > 0) Parsed[token] = groups;
                }
            }

            Validate();
        }

        /// <summary>1.1.2: a station mapped to a group name that does not exist in [ItemGroups] — a
        /// typo, or a group the user renamed — silently attracts nothing, and during Organize it also
        /// makes every chest near that station an anchor for an empty bucket. Nothing reported this.
        /// Warn once per rebuild instead.</summary>
        private static void Validate()
        {
            foreach (var kv in Parsed)
            {
                foreach (var g in kv.Value)
                {
                    if (Groups.IsGroup(g)) continue;
                    Plugin.Log.LogWarning("[stations] '" + kv.Key + "' maps to '" + g +
                        "', which is not a group in [ItemGroups] - that mapping does nothing. " +
                        "Valid groups: " + string.Join(", ", Groups.GroupsInOrder()));
                }
            }
        }

        /// <summary>Group names attracted by a station m_name token, or an empty list.</summary>
        internal static List<string> GroupsForStationName(string mName)
        {
            if (!string.IsNullOrEmpty(mName) && Parsed.TryGetValue(mName, out var groups))
                return groups;
            return Empty;
        }

        /// <summary>Groups attracted by the nearest station-like piece (crafting station OR
        /// smelter/fermenter/cooking station) within <paramref name="range"/> metres of a chest,
        /// or empty. Logs the detected m_name at Info so tokens (including modded ones) can be
        /// verified and added to the config.</summary>
        internal static List<string> GroupsForChest(Container c, float range)
        {
            if (c == null) return Empty;
            var pos = c.transform.position;
            // Nearest MAPPED station wins; unmapped pieces never shadow a mapped one (a campfire's
            // unmapped cooking station must not blank out the cauldron 2 m further away). The
            // nearest unmapped token is still logged as a CustomStations hint when nothing matches.
            string bestMapped = null, bestUnmapped = null;
            float bestMappedD = range, bestUnmappedD = range;

            var all = AllStationsField != null ? AllStationsField.GetValue(null) as List<CraftingStation> : null;
            if (all != null)
            {
                foreach (var st in all)
                {
                    if (st == null || !IsReal(st)) continue;   // vanilla adds placement ghosts to m_allStations too
                    Consider(st.m_name, Vector3.Distance(st.transform.position, pos),
                        ref bestMapped, ref bestMappedD, ref bestUnmapped, ref bestUnmappedD);
                }
            }

            foreach (var kv in Processors)
            {
                if (kv.Key == null) { Dead.Add(kv.Key); continue; }   // unloaded piece
                if (!IsReal(kv.Key)) continue;                        // ZDO died since registration
                Consider(kv.Value, Vector3.Distance(kv.Key.transform.position, pos),
                    ref bestMapped, ref bestMappedD, ref bestUnmapped, ref bestUnmappedD);
            }
            if (Dead.Count > 0)
            {
                foreach (var dead in Dead) Processors.Remove(dead);
                Dead.Clear();
            }

            if (bestMapped != null)
            {
                var groups = GroupsForStationName(bestMapped);
                // 1.1.2: was LogInfo, i.e. one synchronous log write PER CHEST per plan. At a few
                // hundred chests that alone cost tens of ms and buried the actually-useful unmapped
                // hint below. The hint stays at Info, but only the first time we see each token.
                Plugin.Log.LogDebug("[organize] chest near '" + bestMapped + "' (" + bestMappedD.ToString("0.0") + "m) -> " + string.Join(",", groups));
                return groups;
            }
            if (bestUnmapped != null && Hinted.Add(bestUnmapped))
                Plugin.Log.LogInfo("[organize] chest near station '" + bestUnmapped + "' (" + bestUnmappedD.ToString("0.0") +
                    "m) has NO [Stations] mapping - add '" + bestUnmapped + " = <groups>' to the config to route its materials");
            return Empty;
        }

        /// <summary>Station tokens already reported as unmapped, so the hint is logged once per token
        /// per session instead of once per chest per Organize.</summary>
        private static readonly HashSet<string> Hinted = new HashSet<string>();

        /// <summary>A station-like piece found near a point, with the groups it attracts.</summary>
        internal struct StationHit
        {
            internal string Token;            // the piece's m_name
            internal Vector3 Position;
            internal float Distance;
            internal List<string> Groups;     // empty when the token has no [Stations] mapping
        }

        /// <summary>WAVE 0 — every station-like piece within <paramref name="range"/> of a point.
        ///
        /// <see cref="GroupsForChest"/> answers "what does THIS chest inherit", which forces a full
        /// station scan per chest — the single most expensive thing Organize does at scale. Organize
        /// v2 needs the inverse ("where is the forge, so I can claim an empty chest next to it") and
        /// needs to scan once per run rather than once per chest. Both are this call.
        ///
        /// Unlike GroupsForChest this does NOT log and does not pick a winner; it returns everything
        /// in range, nearest first, so the caller can build its own index.</summary>
        internal static List<StationHit> StationsInRange(Vector3 point, float range)
        {
            var hits = new List<StationHit>();

            var all = AllStationsField != null ? AllStationsField.GetValue(null) as List<CraftingStation> : null;
            if (all != null)
            {
                foreach (var st in all)
                {
                    if (st == null || !IsReal(st)) continue;      // vanilla lists placement ghosts too
                    float d = Vector3.Distance(st.transform.position, point);
                    if (d > range) continue;
                    hits.Add(new StationHit
                    {
                        Token = st.m_name,
                        Position = st.transform.position,
                        Distance = d,
                        Groups = GroupsForStationName(st.m_name),
                    });
                }
            }

            foreach (var kv in Processors)
            {
                if (kv.Key == null) { Dead.Add(kv.Key); continue; }
                if (!IsReal(kv.Key)) continue;
                float d = Vector3.Distance(kv.Key.transform.position, point);
                if (d > range) continue;
                hits.Add(new StationHit
                {
                    Token = kv.Value,
                    Position = kv.Key.transform.position,
                    Distance = d,
                    Groups = GroupsForStationName(kv.Value),
                });
            }
            if (Dead.Count > 0)
            {
                foreach (var dead in Dead) Processors.Remove(dead);
                Dead.Clear();
            }

            // Deterministic: distance, then token name for equidistant pieces.
            hits.Sort((a, b) =>
            {
                int d = a.Distance.CompareTo(b.Distance);
                return d != 0 ? d : string.CompareOrdinal(a.Token, b.Token);
            });
            return hits;
        }

        // ---------- W1 (Organize v2): one shared station pass ----------
        // APPEND ONLY (roadmap §4). GroupsForChest answers "what does THIS chest inherit", which costs
        // a full station scan per chest — §16.3 measured that as a top cost at 400 chests, and §15.8's
        // Router parity fix would have put it on the sorter TICK path, once per candidate per item per
        // second. Both callers now share one cached spatial pass and a cheap per-chest lookup over it.

        private static List<StationHit> _cachedHits;
        private static Vector3 _cachedCenter;
        private static float _cachedRadius;
        private static float _cachedAt = -1f;

        /// <summary>Station hits around a point, reusing the previous pass when it still covers the
        /// query and is fresh. The cache is intentionally coarse: stations do not move, and a newly
        /// built one starting to attract items up to <paramref name="ttl"/> seconds late is invisible.</summary>
        internal static List<StationHit> HitsAround(Vector3 center, float radius, float ttl)
        {
            bool usable =
                _cachedHits != null &&
                Time.time - _cachedAt < ttl &&
                _cachedRadius >= radius &&
                Vector3.Distance(_cachedCenter, center) <= Mathf.Max(0f, _cachedRadius - radius);

            if (usable) return _cachedHits;

            _cachedHits = StationsInRange(center, radius);
            _cachedCenter = center;
            _cachedRadius = radius;
            _cachedAt = Time.time;
            return _cachedHits;
        }

        /// <summary>Groups attracted by the nearest MAPPED station within <paramref name="range"/> of a
        /// point, picked out of an already-computed hit list. Same winner and same tie-break as
        /// <see cref="GroupsForChest"/>: nearest mapped station, then the token name, so an equidistant
        /// forge/stonecutter pair resolves identically on every client and every relog.</summary>
        internal static List<string> GroupsNear(List<StationHit> hits, Vector3 pos, float range)
        {
            if (hits == null) return Empty;
            List<string> best = null;
            string bestToken = null;
            float bestD = range;

            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                if (hit.Groups == null || hit.Groups.Count == 0) continue;   // unmapped never shadows
                float d = Vector3.Distance(hit.Position, pos);
                if (d > range) continue;
                if (best == null || d < bestD ||
                    (d == bestD && bestToken != null && string.CompareOrdinal(hit.Token, bestToken) < 0))
                {
                    best = hit.Groups;
                    bestToken = hit.Token;
                    bestD = d;
                }
            }
            return best ?? Empty;
        }

        // Per-chest memo over the cached pass. Router needs this per candidate per item per tick, so
        // even an O(stations) lookup per candidate would be too much; neither chests nor stations move,
        // so a short TTL is safe. Dead keys are pruned opportunistically, like Processors.
        private static readonly Dictionary<Container, KeyValuePair<float, List<string>>> ChestGroups =
            new Dictionary<Container, KeyValuePair<float, List<string>>>();
        private static readonly List<Container> DeadChests = new List<Container>();

        /// <summary>Cached per-chest station groups, for hot paths. <see cref="GroupsForChest"/> stays
        /// the uncached, logging version.</summary>
        internal static List<string> GroupsForChestCached(Container c, Vector3 center, float radius,
                                                          float range, float ttl)
        {
            if (c == null) return Empty;
            if (ChestGroups.TryGetValue(c, out var hit) && Time.time - hit.Key < ttl)
                return hit.Value;

            if (ChestGroups.Count > 256)
            {
                foreach (var kv in ChestGroups) if (kv.Key == null) DeadChests.Add(kv.Key);
                foreach (var d in DeadChests) ChestGroups.Remove(d);
                DeadChests.Clear();
            }

            var hits = HitsAround(center, radius, ttl);
            var groups = GroupsNear(hits, c.transform.position, range);
            ChestGroups[c] = new KeyValuePair<float, List<string>>(Time.time, groups);
            return groups;
        }

        private static void Consider(string mName, float d,
            ref string bestMapped, ref float bestMappedD, ref string bestUnmapped, ref float bestUnmappedD)
        {
            if (string.IsNullOrEmpty(mName)) return;
            bool mapped = Parsed.TryGetValue(mName, out var groups) && groups.Count > 0;
            // 1.1.2: strict '<' meant two equidistant stations (a chest placed exactly between a forge
            // and a stonecutter — a normal crafting-hall layout) were resolved by m_allStations' Awake
            // order, so the chest changed which groups it attracted between sessions. Break the tie on
            // the token name instead: arbitrary, but identical on every client and every relog.
            if (mapped)
            {
                if (d < bestMappedD || (d == bestMappedD && bestMapped != null &&
                    string.CompareOrdinal(mName, bestMapped) < 0))
                { bestMappedD = d; bestMapped = mName; }
            }
            else
            {
                if (d < bestUnmappedD || (d == bestUnmappedD && bestUnmapped != null &&
                    string.CompareOrdinal(mName, bestUnmapped) < 0))
                { bestUnmappedD = d; bestUnmapped = mName; }
            }
        }
    }
}
