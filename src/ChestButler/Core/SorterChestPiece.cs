namespace ChestButler.Core
{
    /// <summary>WAVE 0 STUB — owned by W3 (Dedicated Sorter Chest).
    ///
    /// A craftable chest piece that is a Sorter by default: a Jotunn CustomPiece cloned from the
    /// vanilla reinforced chest (prefab name <c>piece_chest</c> — note <c>piece_chest_wood</c> is the
    /// BASIC chest), registered under <c>PrefabManager.OnVanillaPrefabsAvailable</c> and unsubscribed
    /// after. Spec: <c>docs/roadmap-2.0.md</c> §7 W3.
    ///
    /// Two things the spec calls out and this stub cannot do for you:
    ///
    /// 1. The default must be applied ONCE, not on every load. <c>SorterZdo.IsSorter</c> reads a bool
    ///    that defaults to false, so it cannot tell "never set" from "the player switched it off" —
    ///    writing the flag on every Awake would re-enable Sorter every time the zone reloads and
    ///    override the player. Use <see cref="SorterZdo.WasDefaulted"/> as the marker; the pair of
    ///    helpers is already stubbed for you.
    /// 2. This piece depends on W1: the Organize allocator currently treats every sorter chest as
    ///    excluded, so a base built out of these would be invisible to it.
    ///
    /// Custom prefabs must exist on the server AND every client — this is what makes 2.0 a
    /// coordinated release.</summary>
    internal static class SorterChestPiece
    {
        internal static void Register()
        {
            // W3: subscribe to PrefabManager.OnVanillaPrefabsAvailable and add the CustomPiece here.
        }
    }
}
