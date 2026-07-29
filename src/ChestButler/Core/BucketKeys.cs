using System;

namespace ChestButler.Core
{
    /// <summary>Bucket-key format, shared by the pure allocator and the Unity adapter.
    ///
    /// Organize v2 allocates chests to *buckets*, and a bucket key is a plain string so that group
    /// buckets, gear buckets and per-item-type buckets can all live in one namespace (v2 plan §5).
    /// The formats are fixed here rather than spelled inline, because the keys are also written into
    /// chest ZDOs as the <c>psort_home</c> marker (§4.1) — they are persisted data, not just
    /// in-memory labels, so a careless change to the format silently orphans every marked chest.
    ///
    /// This file is deliberately Unity-free and config-free: it compiles into the offline test
    /// project alongside the planner.</summary>
    internal static class BucketKeys
    {
        /// <summary>Non-stackable gear buckets (v2 plan §5.3 / §16.4.4).</summary>
        internal const string GearPrefix = "gear:";

        internal const string Weapons = GearPrefix + "weapons";
        internal const string Armor = GearPrefix + "armor";
        internal const string Tools = GearPrefix + "tools";

        /// <summary>Gear that maps to no weapon/armor/tool bucket. §16.4.4: the v1 spec made "tools"
        /// the catch-all, which filed a Dragon Egg with the hoes and meant nothing was ever reported
        /// as unmapped. This bucket exists so the catch-all is visible and loggable.</summary>
        internal const string GearMisc = GearPrefix + "misc";

        /// <summary>An ungrouped stackable that earned its own bucket (demand above the promote
        /// threshold). Prefix keeps it from ever colliding with a group name.</summary>
        internal const string TypePrefix = "item:";

        /// <summary>Catch-all for ungrouped stackables too small to deserve a chest (§16.4.1: without
        /// this, 3 Queen Bees + 1 Wisp + 12 Resin claimed three chests for 16 items, and the ungrouped
        /// buckets ate 40–70 chests before `wood` got anything).</summary>
        internal const string Misc = "misc";

        internal static string ForType(string norm) => TypePrefix + norm;

        internal static bool IsGear(string key) =>
            key != null && key.StartsWith(GearPrefix, StringComparison.Ordinal);

        internal static bool IsPerType(string key) =>
            key != null && key.StartsWith(TypePrefix, StringComparison.Ordinal);

        /// <summary>The item norm behind a per-type bucket, or null.</summary>
        internal static string TypeOf(string key) =>
            IsPerType(key) ? key.Substring(TypePrefix.Length) : null;

        /// <summary>Human-readable form for HUD messages and logs ("gear:weapons" → "weapons").</summary>
        internal static string Label(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            if (IsGear(key)) return key.Substring(GearPrefix.Length);
            if (IsPerType(key)) return key.Substring(TypePrefix.Length);
            return key;
        }
    }
}
