using System.Collections.Generic;
using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>Curated crafting-station → item-group map (server-synced config, section [Stations]),
    /// mirroring <see cref="Groups"/>. Keys are the station's <c>CraftingStation.m_name</c> token;
    /// values are comma-separated group names that must exist in [ItemGroups]. During Organize, a
    /// chest sitting next to a station attracts that station's groups.
    ///
    /// VERIFY the m_name tokens in-game: <see cref="GroupsForChest"/> logs the detected station name
    /// at debug level — if a token differs from the defaults below, edit the config to match.
    ///
    /// CAVEAT: smelters, kilns, blast furnaces, windmills and fermenters are Smelter/Fermenter/
    /// Windmill components, NOT CraftingStations, so <c>GetCraftingStation</c> never returns them.
    /// Station adjacency therefore covers only true crafting stations; entries whose token is never
    /// produced by a CraftingStation (e.g. $piece_fermenter) simply stay inert.</summary>
    internal static class Stations
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "$piece_forge",       "metals, ores" },
            { "$piece_workbench",   "wood, hides" },
            { "$piece_stonecutter", "stone" },
            { "$piece_cauldron",    "cooking, meat, seeds" },
            { "$piece_fermenter",   "meads" },              // inert unless a CraftingStation reports this token
            { "$piece_blackforge",  "metals, valuables" },
            { "$piece_magetable",   "valuables, meads" },
        };

        private static readonly Dictionary<string, ConfigEntry<string>> Entries =
            new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, List<string>> Parsed =
            new Dictionary<string, List<string>>();
        private static readonly List<string> Empty = new List<string>();

        internal static void Init(ConfigFile config)
        {
            foreach (var kv in Defaults)
            {
                Entries[kv.Key] = config.Bind("Stations", kv.Key, kv.Value,
                    new ConfigDescription("Comma-separated group names (see [ItemGroups]) this station attracts during Organize.",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }
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
        }

        /// <summary>Group names attracted by a station m_name token, or an empty list.</summary>
        internal static List<string> GroupsForStationName(string mName)
        {
            if (!string.IsNullOrEmpty(mName) && Parsed.TryGetValue(mName, out var groups))
                return groups;
            return Empty;
        }

        /// <summary>Groups attracted by the crafting station within build range of a chest, or empty.
        /// Logs the detected station m_name at debug level so tokens can be verified in-game.</summary>
        internal static List<string> GroupsForChest(Container c)
        {
            if (c == null) return Empty;
            var st = CraftingStation.GetCraftingStation(c.transform.position);
            if (st == null) return Empty;
            var key = st.m_name;
            var groups = GroupsForStationName(key);
            if (groups.Count > 0)
                Plugin.Log.LogDebug("[organize] chest near station '" + key + "' → " + string.Join(",", groups));
            else if (!string.IsNullOrEmpty(key))
                Plugin.Log.LogDebug("[organize] station '" + key + "' has no [Stations] mapping (add one to route its materials)");
            return groups;
        }
    }
}
