using System.Collections.Generic;
using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>Named item groups (server-synced config, section [ItemGroups]).
    /// Values are comma-separated normalized name tokens; '*' wildcards allowed.
    /// Users can edit any group or add tokens for modded items.</summary>
    internal static class Groups
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "stone",     "stone, flint, obsidian, blackmarble, grausten*" },
            { "wood",      "wood, finewood, roundlog, elderbark, yggdrasilwood, blackwood" },
            // NOTE: metal scrap tokens are singular in-game (ironscrap, bronzescrap, copperscrap) —
            // a '*scraps' wildcard misses all of them and instead catches leatherscraps (hides).
            { "ores",      "*ore, ironscrap, bronzescrap, copperscrap, flametalore*" },
            { "metals",    "copper, tin, bronze, iron, silver, blackmetal*, flametal*" },
            { "cooking",   "carrot, turnip, onion, raspberries, blueberries, cloudberries, mushroom*, jotunpuffs, magecap, honey, barley, bread*, sausages" },
            { "meat",      "*meat*, necktail, entrails, bloodbag, fish*" },
            { "seeds",     "*seeds*, acorn, ancientseed, beechnut, carrotseed, turnipseed, onionseed" },
            { "trophies",  "trophy*" },
            { "valuables", "coins, ruby, amber, amberpearl, silvernecklace" },
            { "meads",     "mead*, barleywine*" },
            { "ammo",      "arrow*, bolt*, turretbolt*" },
            { "hides",     "*hide*, *pelt*, leatherscraps, chitin" },
            { "fuel",      "coal" },
        };

        /// <summary>The one authoritative group order (1.1.2). Some items legitimately match two
        /// groups — FlametalOre hits both `ores` ("*ore") and `metals` ("flametal*") in the shipped
        /// defaults — so anything that has to pick ONE group for an item needs a fixed precedence.
        /// It must NOT be dictionary iteration order: that is hash-bucket order, which is stable
        /// within a process but shifts when a group is added, silently re-homing a whole category.
        ///
        /// Order rationale: the narrower, more specific categories come first, so a refined metal is
        /// treated as a metal rather than as ore.</summary>
        private static readonly string[] GroupOrder =
        {
            "metals", "ores", "stone", "wood", "fuel",
            "cooking", "meat", "seeds", "meads",
            "ammo", "hides", "valuables", "trophies",
        };

        private static readonly Dictionary<string, ConfigEntry<string>> Entries =
            new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, List<string>> Parsed =
            new Dictionary<string, List<string>>();
        private static readonly List<string> Ordered = new List<string>();

        internal static void Init(ConfigFile config)
        {
            // Guard against GroupOrder and Defaults drifting apart when a group is added.
            foreach (var name in GroupOrder)
                if (!Defaults.ContainsKey(name))
                    Plugin.Log.LogError("[groups] GroupOrder lists '" + name + "', which is not a defined group");
            foreach (var kv in Defaults)
                if (System.Array.IndexOf(GroupOrder, kv.Key) < 0)
                    Plugin.Log.LogError("[groups] group '" + kv.Key + "' is missing from GroupOrder; " +
                        "overlap resolution would be non-deterministic for its items");

            foreach (var kv in Defaults)
            {
                Entries[kv.Key] = config.Bind("ItemGroups", kv.Key, kv.Value,
                    new ConfigDescription("Comma-separated item name tokens ('*' wildcards allowed).",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }
            config.SettingChanged += (_, __) => Rebuild();
            Rebuild();
        }

        private static void Rebuild()
        {
            Parsed.Clear();
            Ordered.Clear();
            foreach (var kv in Entries)
            {
                var tokens = new List<string>();
                foreach (var raw in kv.Value.Value.Split(','))
                {
                    var t = raw.Trim().ToLowerInvariant();
                    if (t.Length > 0) tokens.Add(t);
                }
                Parsed[kv.Key] = tokens;
            }

            // Deterministic enumeration order: GroupOrder first, then anything not listed there
            // (alphabetically, so it is still stable) so a future group is never simply dropped.
            foreach (var name in GroupOrder)
                if (Parsed.ContainsKey(name)) Ordered.Add(name);
            var rest = new List<string>();
            foreach (var kv in Parsed)
                if (System.Array.IndexOf(GroupOrder, kv.Key) < 0) rest.Add(kv.Key);
            rest.Sort(System.StringComparer.Ordinal);
            Ordered.AddRange(rest);
        }

        internal static bool IsGroup(string name) => Parsed.ContainsKey(name);

        /// <summary>All group names in the authoritative precedence order (see GroupOrder).
        /// Anything that must resolve an item to exactly ONE group iterates this.</summary>
        internal static IReadOnlyList<string> GroupsInOrder() => Ordered;

        /// <summary>The first group (by precedence) whose tokens cover this item, or null.</summary>
        internal static string FirstGroupFor(string normName)
        {
            for (int i = 0; i < Ordered.Count; i++)
                if (GroupContains(Ordered[i], normName)) return Ordered[i];
            return null;
        }

        internal static bool GroupContains(string group, string normName)
        {
            if (!Parsed.TryGetValue(group, out var tokens)) return false;
            foreach (var t in tokens)
                if (Names.Matches(t, normName)) return true;
            return false;
        }
    }
}
