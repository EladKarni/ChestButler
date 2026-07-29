using System.Collections.Generic;
using BepInEx.Configuration;
using ItemType = ItemDrop.ItemData.ItemType;

namespace ChestButler.Core
{
    /// <summary>Classifies NON-STACKABLE gear into the three buckets Organize v2 uses — weapons,
    /// armor, tools — plus a visible <c>gear:misc</c> catch-all.
    ///
    /// Why this file exists at all: <c>[ItemGroups]</c> is a name-token matcher and cannot express
    /// "everything whose <c>m_itemType</c> is Tool", so gear cannot be a normal group (v2 plan
    /// §16.4.4). The primary signal is therefore the item's own <c>m_itemType</c>, which is exact and
    /// covers modded gear for free.
    ///
    /// The enum below was read out of <c>Managed/assembly_valheim.dll</c> rather than taken from the
    /// plan, and two things differ from §5.3's sketch:
    ///
    /// - There is **no** <c>Pickaxe</c> item type. Pickaxes are weapons by <c>m_itemType</c> (they use
    ///   the Pickaxes weapon skill), so a pure type map files a pickaxe with Frostner — exactly the
    ///   misfile §16.4.4 predicted. Hence the token overrides below, which run FIRST.
    /// - The real enum also carries <c>TwoHandedWeaponLeft</c>, <c>Attach_Atgeir</c>, <c>Trinket</c>,
    ///   <c>AmmoNonEquipable</c> and <c>Fish</c>, none of which §5.3 mentions.
    ///
    /// OPEN DEFAULT (veto by editing the config, no rebuild needed — v2 plan §13 style): pickaxes are
    /// filed as TOOLS, axes stay WEAPONS. A pickaxe only ever hits rock; an axe is a real weapon on
    /// its own skill line and "battleaxe" vs "axe" is not a distinction worth surprising anyone with.
    /// Both are just tokens in <c>[Organize] ToolTokens</c>.</summary>
    internal static class Gear
    {
        private static readonly Dictionary<ItemType, string> ByType = new Dictionary<ItemType, string>
        {
            // -- weapons ---------------------------------------------------------------------------
            { ItemType.OneHandedWeapon,     BucketKeys.Weapons },
            { ItemType.TwoHandedWeapon,     BucketKeys.Weapons },
            { ItemType.TwoHandedWeaponLeft, BucketKeys.Weapons },
            { ItemType.Bow,                 BucketKeys.Weapons },
            { ItemType.Shield,              BucketKeys.Weapons },
            { ItemType.Torch,               BucketKeys.Weapons },
            { ItemType.Attach_Atgeir,       BucketKeys.Weapons },

            // -- armor -----------------------------------------------------------------------------
            { ItemType.Helmet,   BucketKeys.Armor },
            { ItemType.Chest,    BucketKeys.Armor },
            { ItemType.Legs,     BucketKeys.Armor },
            { ItemType.Shoulder, BucketKeys.Armor },
            { ItemType.Hands,    BucketKeys.Armor },
            { ItemType.Utility,  BucketKeys.Armor },   // belts, megingjord
            { ItemType.Trinket,  BucketKeys.Armor },   // Ashlands jewellery — equippable, so armor

            // -- tools -----------------------------------------------------------------------------
            { ItemType.Tool, BucketKeys.Tools },
        };

        private static ConfigEntry<string> _weaponTokens, _armorTokens, _toolTokens;
        private static readonly List<string> Weapon = new List<string>();
        private static readonly List<string> Armour = new List<string>();
        private static readonly List<string> Tool = new List<string>();

        /// <summary>Item norms already reported as landing in the catch-all, so the log line is
        /// emitted once per type per session rather than once per stack per Organize.</summary>
        private static readonly HashSet<string> Reported = new HashSet<string>();

        internal static void Init(ConfigFile config)
        {
            _toolTokens = config.Bind("Organize", "ToolTokens", "pickaxe*",
                new ConfigDescription(
                    "Item name tokens ('*' wildcards) forced into the TOOLS gear bucket, overriding the item's own type. " +
                    "Default: pickaxes, which the game classifies as weapons. Comma-separated.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            _weaponTokens = config.Bind("Organize", "WeaponTokens", "",
                new ConfigDescription("Item name tokens forced into the WEAPONS gear bucket. Comma-separated.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            _armorTokens = config.Bind("Organize", "ArmorTokens", "",
                new ConfigDescription("Item name tokens forced into the ARMOR gear bucket. Comma-separated.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            config.SettingChanged += (_, __) => Rebuild();
            Rebuild();
        }

        private static void Rebuild()
        {
            Fill(Weapon, _weaponTokens);
            Fill(Armour, _armorTokens);
            Fill(Tool, _toolTokens);
            Reported.Clear();   // token change can re-home a type; let it report again
        }

        private static void Fill(List<string> into, ConfigEntry<string> entry)
        {
            into.Clear();
            if (entry == null || string.IsNullOrEmpty(entry.Value)) return;
            foreach (var raw in entry.Value.Split(','))
            {
                var t = raw.Trim().ToLowerInvariant();
                if (t.Length > 0) into.Add(t);
            }
        }

        /// <summary>The gear bucket for a non-stackable item. Never returns null: anything the type
        /// map does not cover lands in <c>gear:misc</c> and is logged once, so an unmapped modded item
        /// type is a visible fact rather than a silent misfile into "tools".</summary>
        internal static string BucketFor(ItemDrop.ItemData item, string norm)
        {
            if (item?.m_shared == null) return BucketKeys.GearMisc;

            // Token overrides win over the item's own type — that is the whole point of them.
            if (MatchesAny(Tool, norm)) return BucketKeys.Tools;
            if (MatchesAny(Weapon, norm)) return BucketKeys.Weapons;
            if (MatchesAny(Armour, norm)) return BucketKeys.Armor;

            if (ByType.TryGetValue(item.m_shared.m_itemType, out var bucket)) return bucket;

            if (Reported.Add(norm))
                Plugin.Log.LogInfo("[organize] '" + norm + "' (item type " + item.m_shared.m_itemType +
                    ") has no gear bucket - filed under " + BucketKeys.Label(BucketKeys.GearMisc) +
                    ". Add it to [Organize] WeaponTokens/ArmorTokens/ToolTokens to place it.");
            return BucketKeys.GearMisc;
        }

        private static bool MatchesAny(List<string> tokens, string norm)
        {
            for (int i = 0; i < tokens.Count; i++)
                if (Names.Matches(tokens[i], norm)) return true;
            return false;
        }
    }
}
