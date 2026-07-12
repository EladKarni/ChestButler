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
            { "ores",      "*ore, *scraps, flametalore*" },
            { "metals",    "copper, tin, bronze, iron, silver, blackmetal*, flametal*" },
            { "cooking",   "carrot, turnip, onion, raspberries, blueberries, cloudberries, mushroom*, jotunpuffs, magecap, honey, barley, bread*, sausages" },
            { "meat",      "*meat*, necktail, entrails, bloodbag, fish*" },
            { "seeds",     "*seeds*, acorn, ancientseed, beechnut, carrotseed, turnipseed, onionseed" },
            { "trophies",  "trophy*" },
            { "valuables", "coins, ruby, amber, amberpearl, silvernecklace" },
            { "meads",     "mead*, barleywine*" },
            { "ammo",      "arrow*, bolt*, turretbolt*" },
            { "hides",     "*hide*, *pelt*, leatherscraps, chitin" },
        };

        private static readonly Dictionary<string, ConfigEntry<string>> Entries =
            new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, List<string>> Parsed =
            new Dictionary<string, List<string>>();

        internal static void Init(ConfigFile config)
        {
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
        }

        internal static bool IsGroup(string name) => Parsed.ContainsKey(name);

        internal static bool GroupContains(string group, string normName)
        {
            if (!Parsed.TryGetValue(group, out var tokens)) return false;
            foreach (var t in tokens)
                if (Names.Matches(t, normName)) return true;
            return false;
        }
    }
}
