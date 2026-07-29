using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>Owns the <c>[Organize]</c> config section.
    ///
    /// WAVE 0 (2.0 groundwork): these entries used to be declared as fields on <see cref="Plugin"/> and
    /// bound inside <c>Plugin.Awake</c>. Organize v2 (W1) needs to add several more, and every other
    /// 2.0 workstream also has to add one line to <c>Plugin.Awake</c> — so the config block moved here,
    /// where exactly one workstream edits it. <see cref="Plugin"/> keeps forwarding properties so
    /// existing readers keep compiling unchanged.
    ///
    /// W1: add new [Organize] entries in this file, not in Plugin.cs.</summary>
    internal static class OrganizeConfig
    {
        internal static ConfigEntry<float> StationRange;

        /// <summary>W1 (§16.3): replaces the old per-FRAME <c>MovesPerTick</c>. Per-frame made the RPC
        /// rate framerate-dependent — 4/tick is 240 RPC/s at 60 fps but 2,304 RPC/s at 144 fps, from a
        /// single client — which is a server-load bug disguised as a preference.</summary>
        internal static ConfigEntry<int> MovesPerSecond;

        /// <summary>W1 (§16.3): one click must not be able to fire thousands of RPCs. When the cap is
        /// reached the run stops and says "press Organize again to continue"; the next press re-plans
        /// from scratch, which is both cheaper to reason about and safer than resuming a stale queue.</summary>
        internal static ConfigEntry<int> MaxMovesPerRun;

        /// <summary>W1 (v2 plan §9): sweep non-stackable gear into the weapons/armor/tools buckets.
        /// Off restores the 1.1.x behaviour of leaving gear wherever it is unless a chest pins it.</summary>
        internal static ConfigEntry<bool> IncludeGear;

        /// <summary>W1 (§16.4.1): an ungrouped item type only earns its own chest when its slot demand
        /// exceeds this; below it, it shares the <c>misc</c> bucket. Without a threshold, 3 Queen Bees
        /// + 1 Wisp + 12 Resin claimed three 24-slot chests for 16 items, and the ungrouped buckets ate
        /// 40–70 chests before <c>wood</c> got anything.</summary>
        internal static ConfigEntry<int> MiscPromoteSlots;

        // Null-safe readers, so Organizer works even if a bind failed.
        internal static int MovesPerSecondValue => MovesPerSecond != null ? MovesPerSecond.Value : 25;
        internal static int MaxMovesPerRunValue => MaxMovesPerRun != null ? MaxMovesPerRun.Value : 500;

        internal static void Init(ConfigFile config)
        {
            // Rate only, so client-side and NOT admin-only (see the note in Plugin.Awake). The mod also
            // self-throttles (Core/Throttle.cs, v2 plan §16.6), which makes this a CEILING rather than a
            // tuning dial: it can only ever be scaled down from here, never up.
            MovesPerSecond = config.Bind("Organize", "MovesPerSecond", 25,
                new ConfigDescription("How many item transfers per second the Organize sweep issues (a real per-second rate, " +
                    "independent of your framerate). The mod measures its own cost and backs off below this on its own. " +
                    "Client-side: lower it if Organize costs you frames.",
                    new AcceptableValueRange<int>(5, 100)));

            MaxMovesPerRun = config.Bind("Organize", "MaxMovesPerRun", 500,
                new ConfigDescription("Safety cap on transfers per Organize press. On a very large base the run stops at this " +
                    "many and tells you to press Organize again to continue. Client-side.",
                    new AcceptableValueRange<int>(50, 5000)));

            StationRange = config.Bind("Organize", "StationRange", 8f,
                new ConfigDescription("Max distance (m) from a chest to a crafting station for the chest to inherit that station's item groups during Organize. Nearest mapped station wins. " +
                    "NOTE: this sets the match distance only - station detection scans every loaded station regardless.",
                    new AcceptableValueRange<float>(1f, 20f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            // Result-affecting → admin-only and server-synced: two clients computing different answers
            // for the same base is a correctness bug, not a preference (§16.3).
            IncludeGear = config.Bind("Organize", "IncludeGear", true,
                new ConfigDescription("Sweep weapons, armor and tools into their own chests during Organize. " +
                    "Off leaves gear where it is unless a chest explicitly pins it (the 1.1.x behaviour).",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            MiscPromoteSlots = config.Bind("Organize", "MiscPromoteSlots", 24,
                new ConfigDescription("An ungrouped item type gets its own chest(s) only when it needs more than this many " +
                    "slots; smaller piles share a 'misc' chest. Default is one vanilla chest. " +
                    "An item a chest explicitly pins always gets its own home regardless.",
                    new AcceptableValueRange<int>(1, 120),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            // Gear's token overrides are [Organize] entries too, so they are bound from here rather
            // than adding another line to Plugin.Awake.
            Gear.Init(config);
        }
    }
}
