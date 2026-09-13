using System.Reflection;
using HarmonyLib;
using ChestButler.Core;

namespace ChestButler.Patches
{
    /// <summary>2.1.1 — the click that would have failed fetches instead.
    ///
    /// Press place on a piece you cannot afford and vanilla prints "$msg_missingrequirement" and does
    /// nothing else. This runs just before that check and pulls the shortfall out of nearby chests, so
    /// the normal build flow simply keeps working while there is stock in range.
    ///
    /// Timing is the whole trick. MultiUserChest applies a removal INLINE when the local player owns
    /// the chest's ZDO, which is the usual case for your own base and always the case in single player:
    /// the items are in the inventory before this prefix returns, so vanilla's own check passes on the
    /// same click and the piece goes down immediately. When another peer owns the chest the transfer is
    /// an RPC and lands a frame or more later, so that first click still fails and the second places.
    /// Re-arming the click ourselves is deliberately not attempted: the ghost has moved by then, and
    /// placing where the player is no longer pointing is worse than asking for a second click.
    ///
    /// Everything read here is public API (InPlaceMode, GetSelectedPiece, HaveRequirements,
    /// GetPlacementStatus, PlacementCostDisabled). UpdatePlacement itself is private, hence
    /// TargetMethod rather than a typed attribute, and HaveRequirements is deliberately NOT patched:
    /// four per-frame UI callers share it, and overriding it would make the build menu's own icon
    /// tinting lie.</summary>
    [HarmonyPatch]
    internal static class PlaceGatherPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) });

        [HarmonyPrefix]
        private static void Prefix(Player __instance, bool takeInput)
        {
            if (!Gather.IsEnabled || !Gather.IsBuildEnabled) return;
            if (__instance == null || __instance != Player.m_localPlayer) return;

            // Cheapest early-outs first: this runs every frame the local player is alive.
            if (!takeInput || !__instance.InPlaceMode()) return;
            if (Hud.IsPieceSelectionVisible()) return;      // vanilla ignores placement input while the menu is up
            if (__instance.PlacementCostDisabled) return;   // nocost/debug builds need no materials at all

            // The place edge, not the held button: vanilla registers Attack and JoyPlace with no key
            // repeat, so this fires once per physical press. AltPlace is the copy-piece gesture, which
            // is not a placement and must not trigger a fetch.
            if (!ZInput.GetButtonDown("Attack") && !ZInput.GetButtonDown("JoyPlace")) return;
            if (ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltKeys")) return;

            var piece = __instance.GetSelectedPiece();
            if (piece == null || piece.m_resources == null || piece.m_resources.Length == 0) return;
            if (__instance.HaveRequirements(piece, Player.RequirementMode.CanBuild)) return;   // affordable already

            // Only fetch for a spot that would actually take the piece. Vanilla checks cost before
            // validity, so without this a click at an illegal spot would haul materials for nothing.
            if (__instance.GetPlacementStatus() != Player.PlacementStatus.Valid) return;

            // A click deserves a fresh answer: the shared chest list is cached for a second, which is
            // right for a per-frame HUD and wrong here.
            BuildGather.Invalidate();

            var needs = GatherMath.Resolve(BuildGather.NeedsFor(piece), false);
            if (needs.Count == 0) return;                  // nothing in range; vanilla's own message stands

            Gatherer.Pull(needs, out int moved, out int types);
            BuildGather.Invalidate();

            if (moved > 0)
                Plugin.Log.LogInfo("[gather] place-triggered pull: " + moved + " item(s), " +
                                   types + " type(s) for " + piece.name);
        }
    }
}
