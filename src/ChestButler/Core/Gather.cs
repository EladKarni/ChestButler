using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>Owns the <c>[Gather]</c> config section (W2).
    ///
    /// Gather adds a button beside the craft button in the crafting panel that pulls a recipe's missing
    /// ingredients out of nearby storage into the player's inventory, and shows an "(N in storage)"
    /// count per ingredient. Plan: <c>docs/gather-plan.md</c>.
    ///
    /// Both entries are client-side and NOT admin-only: they change what THIS player's UI offers, never
    /// what any shared outcome is. The search radius deliberately reuses <c>Plugin.SorterRadius</c>
    /// rather than adding a second, silently-different range for the same question of "which chests can
    /// I reach".</summary>
    internal static class Gather
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> BuildEnabled;
        internal static ConfigEntry<bool> ShowStorageCounts;

        internal static bool IsEnabled => Enabled == null || Enabled.Value;
        internal static bool IsBuildEnabled => BuildEnabled == null || BuildEnabled.Value;
        internal static bool CountsShown => ShowStorageCounts == null || ShowStorageCounts.Value;

        internal static void Init(ConfigFile config)
        {
            Enabled = config.Bind("Gather", "Enabled", true,
                new ConfigDescription("Show the Gather button in the crafting panel. Client-side."));

            // 2.1.1: build gathering is gated separately from the crafting button. Someone who wants
            // materials fetched at the bench does not necessarily want a place click reaching into
            // their chests.
            BuildEnabled = config.Bind("Gather", "BuildEnabled", true,
                new ConfigDescription("When you place a piece you can't afford, fetch the missing materials from nearby chests. Client-side."));

            ShowStorageCounts = config.Bind("Gather", "ShowStorageCounts", true,
                new ConfigDescription("Show \"(N in storage)\" beside each ingredient in the crafting panel. Client-side."));
        }
    }
}
