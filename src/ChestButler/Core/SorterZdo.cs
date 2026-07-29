using System;
using HarmonyLib;

namespace ChestButler.Core
{
    /// <summary>Per-chest sorter state, persisted in the container's own ZDO
    /// (syncs to all peers and saves with the world).</summary>
    internal static class SorterZdo
    {
        private static readonly int SorterHash = "psort_sorter".GetStableHashCode();

        private static readonly AccessTools.FieldRef<Container, ZNetView> NViewRef =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");

        private static readonly Func<Container, long, bool> CheckAccessFunc =
            AccessTools.MethodDelegate<Func<Container, long, bool>>(
                AccessTools.Method(typeof(Container), "CheckAccess"));

        internal static ZNetView NView(Container c) => c ? NViewRef(c) : null;

        internal static bool HasValidNView(Container c)
        {
            var nv = NView(c);
            return nv != null && nv.IsValid();
        }

        internal static bool IsSorter(Container c)
        {
            var nv = NView(c);
            return nv != null && nv.IsValid() && nv.GetZDO().GetBool(SorterHash, false);
        }

        /// <summary>Toggle sorter state. Safe to call from the chest UI: the opener
        /// holds ZDO ownership (vanilla RPC_RequestOpen), but claim defensively.</summary>
        internal static void SetSorter(Container c, bool on)
        {
            var nv = NView(c);
            if (nv == null || !nv.IsValid()) return;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(SorterHash, on);
        }

        // ---------- WAVE 0 STUBS — owned by W3 (Dedicated Sorter Chest) ----------
        // Two keys, not one: a spawn-default that only writes SorterHash would re-enable Sorter on
        // every zone reload, because GetBool cannot distinguish "never set" from "player turned it
        // off". WasDefaulted records that the default has already been applied once.
        //
        // Rule for this file (see roadmap §4): APPEND ONLY, at the end of the class. Do not reorder
        // or reformat existing members — more than one workstream lands here.

        private static readonly int DefaultedHash = "psort_defaulted".GetStableHashCode();

        /// <summary>Has this chest already had its spawn-time Sorter default applied?</summary>
        internal static bool WasDefaulted(Container c)
        {
            var nv = NView(c);
            return nv != null && nv.IsValid() && nv.GetZDO().GetBool(DefaultedHash, false);
        }

        /// <summary>Apply the "this piece is a Sorter out of the box" default exactly once, then
        /// leave the player in control of the toggle forever after.</summary>
        internal static void SetSorterDefault(Container c, bool on)
        {
            var nv = NView(c);
            if (nv == null || !nv.IsValid()) return;
            if (WasDefaulted(c)) return;                  // already decided; never override the player
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(SorterHash, on);
            nv.GetZDO().Set(DefaultedHash, true);
        }

        /// <summary>Vanilla per-container access check (private chests etc.) for the local player.</summary>
        internal static bool PlayerCanAccess(Container c)
        {
            var p = Player.m_localPlayer;
            return p != null && CheckAccessFunc(c, p.GetPlayerID());
        }
    }
}
