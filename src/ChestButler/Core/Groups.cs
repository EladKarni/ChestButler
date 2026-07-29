using System.Collections.Generic;
using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>Named item groups (server-synced config, section [ItemGroups]).
    /// Values are comma-separated normalized name tokens; '*' wildcards allowed.
    /// Users can edit any group or add tokens for modded items.</summary>
    internal static class Groups
    {
        // W1: the tables moved to Core/GroupTables.cs so the offline suite can assert GroupOrder and
        // the group table cannot drift apart (§16.4.3). Behaviour here is unchanged.
        private static Dictionary<string, string> Defaults => GroupTables.Defaults;
        private static string[] GroupOrder => GroupTables.GroupOrder;

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
