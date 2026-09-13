using HarmonyLib;

namespace ChestButler.Patches
{
    /// <summary>2.1.1 — tells the crafting Gather apart from the hammer's build HUD.
    ///
    /// Hud.SetupPieceInfo fills the build cost rows by calling InventoryGui.SetupRequirement, which is
    /// the very callback <see cref="GatherPatch"/> listens to for the crafting panel. Without this flag
    /// a piece's materials land in the crafting need list and get fetched by the craft-side button.
    ///
    /// Build gathering itself has no UI: <see cref="PlaceGatherPatch"/> fetches on the place click.
    /// A build-menu button and a right-click on piece tiles were both tried and dropped. The button
    /// could not know which piece was meant, because its target followed the hover, and right-click
    /// closes the build menu in vanilla.</summary>
    [HarmonyPatch(typeof(Hud))]
    internal static class BuildGatherPatch
    {
        /// <summary>True only while Hud.SetupPieceInfo is filling the build HUD's cost rows.</summary>
        internal static bool InBuildInfo { get; private set; }

        [HarmonyPrefix, HarmonyPatch("SetupPieceInfo")]
        private static void SetupPieceInfoPrefix() => InBuildInfo = true;

        [HarmonyPostfix, HarmonyPatch("SetupPieceInfo")]
        private static void SetupPieceInfoPostfix() => InBuildInfo = false;
    }
}
