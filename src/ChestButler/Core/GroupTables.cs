using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>The item-group tables as pure data — no BepInEx, no Unity.
    ///
    /// W1 split these out of <see cref="Groups"/> for one reason: §16.4.3 asks for "an explicit
    /// <c>static readonly string[] GroupOrder</c> literal + a unit test asserting it covers every
    /// group", and that test could not exist while the tables lived in a file that needs a
    /// <c>ConfigFile</c> to compile. <see cref="Groups"/> still owns binding, parsing and matching;
    /// this file only holds the constants, so the offline suite can check them.</summary>
    internal static class GroupTables
    {
        internal static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
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
        /// groups — FlametalOre hits both <c>ores</c> ("*ore") and <c>metals</c> ("flametal*") in the
        /// shipped defaults — so anything that has to pick ONE group for an item needs a fixed
        /// precedence. It must NOT be dictionary iteration order: that is hash-bucket order, which is
        /// stable within a process but shifts when a group is added, silently re-homing a whole
        /// category after a mod update.
        ///
        /// Order rationale: the narrower, more specific categories come first, so a refined metal is
        /// treated as a metal rather than as ore.</summary>
        internal static readonly string[] GroupOrder =
        {
            "metals", "ores", "stone", "wood", "fuel",
            "cooking", "meat", "seeds", "meads",
            "ammo", "hides", "valuables", "trophies",
        };
    }
}
