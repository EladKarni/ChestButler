using HarmonyLib;
using ChestButler.Core;

namespace ChestButler.Patches
{
    [HarmonyPatch(typeof(Sign), "Awake")]
    internal static class Sign_Awake_Patch
    {
        private static void Postfix(Sign __instance) => Filters.RegisterSign(__instance);
    }

    /// <summary>1.1.2: the filter cache used to rely purely on a 3 s TTL, so every sorter tick
    /// re-parsed nearby signs — an O(chests^2) path, since resolving "which chest owns this sign"
    /// scans every container. Sign edits now invalidate the cache explicitly, which is what lets the
    /// TTL be long without a "sort:" edit taking 30 s to take effect.
    ///
    /// Only SetText is hooked — it is the write path (Interact/UseItem funnel into it). UpdateText
    /// is deliberately NOT hooked: it is reachable from hover/refresh code, so invalidating there
    /// could clear the cache every frame a player looks at a sign. A remote peer's edit arrives by
    /// ZDO sync and is picked up when the TTL lapses.</summary>
    [HarmonyPatch(typeof(Sign), "SetText")]
    internal static class Sign_SetText_Patch
    {
        private static void Postfix() => Filters.InvalidateAll();
    }
}
