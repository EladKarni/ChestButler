using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;

namespace ChestButler.Core
{
    /// <summary>W3 — the Dedicated Sorter Chest: a craftable chest that is a Sorter out of the box.
    ///
    /// A Jotunn <see cref="CustomPiece"/> cloned from the vanilla REINFORCED chest. The prefab name is
    /// <c>piece_chest</c> — counter-intuitively, <c>piece_chest_wood</c> is the *basic* chest — which the
    /// 2.0 audit confirmed and which is worth restating here because getting it wrong silently clones
    /// the wrong container size.
    ///
    /// Nothing else in the mod needs to know this piece exists. It is still a <see cref="Container"/>
    /// with a <see cref="Piece"/>, so <c>ContainerPatches</c> registers it with
    /// <see cref="ContainerTracker"/> and attaches a <see cref="SorterBehaviour"/> automatically, and
    /// the existing chest UI works on it unchanged.
    ///
    /// Custom prefabs must exist on the server AND on every client — this piece is the reason 2.0 is a
    /// coordinated release, and removing the mod later orphans any placed copies.</summary>
    internal static class SorterChestPiece
    {
        internal const string PrefabName = "ChestButler_SorterChest";
        private const string ClonedFrom = "piece_chest";      // the REINFORCED chest, not piece_chest_wood

        private static readonly int PrefabHash = PrefabName.GetStableHashCode();
        private static bool _registered;

        internal static void Register()
        {
            // Vanilla prefabs are not loaded when Plugin.Awake runs, so the clone has to wait for them.
            PrefabManager.OnVanillaPrefabsAvailable += AddPiece;
        }

        private static void AddPiece()
        {
            // Unsubscribe first: the event can fire again on a world change, and adding the same piece
            // name twice makes Jotunn log an error and drop the second one.
            PrefabManager.OnVanillaPrefabsAvailable -= AddPiece;
            if (_registered) return;

            try
            {
                var config = new PieceConfig
                {
                    Name = "Sorter Chest",
                    Description = "A reinforced chest that sorts its contents into nearby chests.",
                    PieceTable = "Hammer",
                    Category = "Furniture",
                    CraftingStation = "piece_workbench",
                    // Icon deliberately left unset: cloning piece_chest copies its Piece component, so
                    // the piece inherits the vanilla reinforced-chest icon and is legible in the build
                    // menu with no art pipeline. A distinct icon is polish, not a blocker — see
                    // docs/sorter-chest-plan.md §5.
                };
                config.AddRequirement(new RequirementConfig("Wood", 10, 0, true));
                config.AddRequirement(new RequirementConfig("BronzeNails", 5, 0, true));

                var piece = new CustomPiece(PrefabName, ClonedFrom, config);
                if (!PieceManager.Instance.AddPiece(piece))
                {
                    Plugin.Log.LogWarning("[sorterchest] Jotunn refused the piece; it will not be buildable");
                    return;
                }

                _registered = true;
                Plugin.Log.LogInfo("[sorterchest] registered '" + PrefabName + "' (cloned from " + ClonedFrom + ")");
            }
            catch (System.Exception e)
            {
                // A failed clone must not take the rest of the mod down with it: sorting, Organize and
                // Gather are all independent of this piece existing.
                Plugin.Log.LogError("[sorterchest] could not register the piece: " + e);
            }
        }

        /// <summary>Is this container one of our Sorter Chests? Matched on the ZDO's prefab hash rather
        /// than on the GameObject name, which picks up "(Clone)" suffixes and varies by spawn path.</summary>
        internal static bool IsSorterChest(Container c)
        {
            var nv = SorterZdo.NView(c);
            if (nv == null || !nv.IsValid()) return false;
            return nv.GetZDO().GetPrefab() == PrefabHash;
        }

        /// <summary>Turn the Sorter flag on the first time one of these is placed, and never again.
        ///
        /// The two-key dance matters (roadmap §7 W3): <see cref="SorterZdo.IsSorter"/> reads a bool that
        /// defaults to false, so it cannot distinguish "never set" from "the player switched it off".
        /// Writing the flag on every Awake would re-enable Sorter on every zone reload and quietly
        /// overrule the player — so <see cref="SorterZdo.SetSorterDefault"/> guards on a separate
        /// <c>WasDefaulted</c> marker and only ever fires once per chest.
        ///
        /// Gated on ownership as well: the client that places the piece owns its fresh ZDO, so this
        /// applies the default exactly where the decision belongs and never claims a ZDO from a peer
        /// just because their chest loaded into our zone.</summary>
        [HarmonyPatch(typeof(Container), "Awake")]
        private static class SpawnDefaultPatch
        {
            private static void Postfix(Container __instance)
            {
                if (__instance == null) return;
                if (!IsSorterChest(__instance)) return;

                var nv = SorterZdo.NView(__instance);
                if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
                if (SorterZdo.WasDefaulted(__instance)) return;

                SorterZdo.SetSorterDefault(__instance, true);
                Plugin.Log.LogInfo("[sorterchest] applied the Sorter default to a newly placed Sorter Chest");
            }
        }
    }
}
