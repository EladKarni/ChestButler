using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Curated crafting-station → item-group map (server-synced config, section [Stations]),
    /// mirroring <see cref="Groups"/>. Keys are the station's <c>CraftingStation.m_name</c> token;
    /// values are comma-separated group names that must exist in [ItemGroups]. During Organize, a
    /// chest sitting next to a station inherits that station's groups.
    ///
    /// Detection: we scan <c>CraftingStation.m_allStations</c> for the NEAREST station within
    /// <see cref="Plugin.StationRange"/> metres of the chest. (We deliberately do NOT use
    /// <c>CraftingStation.GetCraftingStation</c> — that only matches a ~0.1 m "StationUseArea"
    /// trigger, i.e. the exact tile you stand on to craft, so an adjacent chest is never detected.)
    ///
    /// MODDED stations (e.g. a "blacksmith workshop") have their own m_name and are NOT in the
    /// defaults — the Info-level log below prints the detected token so you can add it to the config.
    ///
    /// CAVEAT: smelters, kilns, blast furnaces, windmills and fermenters are Smelter/Fermenter/
    /// Windmill components, NOT CraftingStations, so they are never detected. Route their materials
    /// with a chest pin (or a `sort: group` sign) instead.</summary>
    internal static class Stations
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "$piece_forge",       "metals, ores" },
            { "$piece_workbench",   "wood, hides" },
            { "$piece_stonecutter", "stone" },
            { "$piece_cauldron",    "cooking, meat, seeds" },
            { "$piece_fermenter",   "meads" },              // inert: fermenter is not a CraftingStation
            { "$piece_blackforge",  "metals, valuables" },
            { "$piece_magetable",   "valuables, meads" },
        };

        private static readonly Dictionary<string, ConfigEntry<string>> Entries =
            new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, List<string>> Parsed =
            new Dictionary<string, List<string>>();
        private static readonly List<string> Empty = new List<string>();
        private static ConfigEntry<string> Extra;   // free-form mappings for modded stations

        // private static List<CraftingStation> m_allStations
        private static readonly FieldInfo AllStationsField =
            AccessTools.Field(typeof(CraftingStation), "m_allStations");

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
        }

        /// <summary>Group names attracted by a station m_name token, or an empty list.</summary>
        internal static List<string> GroupsForStationName(string mName)
        {
            if (!string.IsNullOrEmpty(mName) && Parsed.TryGetValue(mName, out var groups))
                return groups;
            return Empty;
        }

        /// <summary>Groups attracted by the nearest crafting station within <paramref name="range"/>
        /// metres of a chest, or empty. Logs the detected station m_name at Info so tokens (including
        /// modded ones) can be verified and added to the config.</summary>
        internal static List<string> GroupsForChest(Container c, float range)
        {
            if (c == null || AllStationsField == null) return Empty;
            var all = AllStationsField.GetValue(null) as List<CraftingStation>;
            if (all == null || all.Count == 0) return Empty;

            var pos = c.transform.position;
            CraftingStation best = null;
            float bestD = range;
            foreach (var st in all)
            {
                if (st == null) continue;
                float d = Vector3.Distance(st.transform.position, pos);
                if (d < bestD) { bestD = d; best = st; }
            }
            if (best == null) return Empty;

            var key = best.m_name;
            var groups = GroupsForStationName(key);
            if (groups.Count > 0)
                Plugin.Log.LogInfo("[organize] chest near '" + key + "' (" + bestD.ToString("0.0") + "m) -> " + string.Join(",", groups));
            else if (!string.IsNullOrEmpty(key))
                Plugin.Log.LogInfo("[organize] chest near station '" + key + "' (" + bestD.ToString("0.0") +
                    "m) has NO [Stations] mapping - add '" + key + " = <groups>' to the config to route its materials");
            return groups;
        }
    }
}
