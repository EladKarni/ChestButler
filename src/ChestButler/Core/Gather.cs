using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>WAVE 0 STUB — owned by W2 (Gather).
    ///
    /// Gather adds a button beside the craft button in the crafting panel that pulls a recipe's
    /// missing ingredients out of nearby storage and into the player's inventory, and shows an
    /// "(N in storage)" count per ingredient. Spec: <c>docs/roadmap-2.0.md</c> §7 W2.
    ///
    /// W2 fills this in and adds <c>Patches/GatherPatch.cs</c> + <c>Core/Gatherer.cs</c>. Nothing
    /// else in the plugin should need editing: <c>Plugin.Awake</c> already calls Init below.
    ///
    /// Before writing code, check the two API corrections in the roadmap: InventoryGui's selected
    /// recipe is a pair (use <c>.Recipe</c> / <c>.ItemData</c>, not the field directly), and the
    /// MultiUserChest transfer primitive takes a destination ZDOID — the player inventory has no
    /// container ZDO, so the chest→player path must be verified before assuming Puller can be
    /// copied.</summary>
    internal static class Gather
    {
        internal static void Init(ConfigFile config)
        {
            // W2: declare and bind the [Gather] entries here (an enable toggle at minimum; reuse
            // Plugin.SorterRadius for the search radius rather than adding a second one).
        }
    }
}
