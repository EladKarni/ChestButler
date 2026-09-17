using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>2.1.2 — the Puller Chest: the Sorter Chest in reverse. Open it, pick an item from the
    /// list of everything in storage around it, and that item is pulled INTO this chest.
    ///
    /// Registered exactly like <see cref="SorterChestPiece"/> (a Jotunn clone of the reinforced chest,
    /// <c>piece_chest</c>), so ContainerTracker picks it up like any other chest. What makes it a
    /// Puller is only its prefab: <see cref="Filters.GetSpec"/> marks it Ignore, the same as a
    /// <c>sort: off</c> chest, so the sorter, Organize, Gather and Pull all leave what you pulled
    /// alone until you take it out. The panel itself lives in <c>PullerPanelPatch</c>.
    ///
    /// Shipped in a patch release on purpose. VersionStrictness.Minor lets a 2.1.1 client join, and that
    /// client has no prefab for this piece, so it simply does not see placed Puller Chests. The owner
    /// accepted that over forcing a coordinated minor release.</summary>
    internal static class PullerChestPiece
    {
        internal const string PrefabName = "ChestButler_PullerChest";
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
            // Same reason as the Sorter Chest: the event can fire again on a world change, and Jotunn
            // drops a second piece with the same name with an error.
            PrefabManager.OnVanillaPrefabsAvailable -= AddPiece;
            if (_registered) return;

            try
            {
                var config = new PieceConfig
                {
                    Name = "Puller Chest",
                    Description = "A reinforced chest that fetches the items you pick from nearby chests.",
                    PieceTable = "Hammer",
                    Category = "Furniture",
                    CraftingStation = "piece_workbench",
                };
                config.AddRequirement(new RequirementConfig("Wood", 10, 0, true));
                config.AddRequirement(new RequirementConfig("BronzeNails", 5, 0, true));

                var piece = new CustomPiece(PrefabName, ClonedFrom, config);
                MakeDistinct(piece, config.Name);

                if (!PieceManager.Instance.AddPiece(piece))
                {
                    Plugin.Log.LogWarning("[pullerchest] Jotunn refused the piece; it will not be buildable");
                    return;
                }

                _registered = true;
                Plugin.Log.LogInfo("[pullerchest] registered '" + PrefabName + "' (cloned from " + ClonedFrom + ")");
            }
            catch (System.Exception e)
            {
                // Everything else in the mod works without this piece.
                Plugin.Log.LogError("[pullerchest] could not register the piece: " + e);
            }
        }

        /// <summary>Blue-grey tint on the model.
        ///
        /// Both custom chests are clones of the reinforced chest, so without this the Puller looked
        /// exactly like the Sorter Chest in the world and in the build menu. The tint goes on COPIES of
        /// the materials: the clone shares the vanilla chest's material assets, and tinting those would
        /// recolour every reinforced chest in the game. The build menu icon is then rendered from the
        /// tinted model instead of inheriting the vanilla one. If rendering fails the vanilla icon stays,
        /// which is the old behaviour rather than a broken piece.</summary>
        private static readonly Color Tint = new Color(0.55f, 0.72f, 1f, 1f);

        private static void MakeDistinct(CustomPiece piece, string name)
        {
            var prefab = piece.PiecePrefab;
            if (prefab == null) return;

            var container = prefab.GetComponent<Container>();
            if (container != null) container.m_name = name;   // hover text and panel title

            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // models only, not effects
                var shared = r.sharedMaterials;
                var copies = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    copies[i] = new Material(shared[i]);
                    if (copies[i].HasProperty("_Color"))
                        copies[i].color = shared[i].color * Tint;
                }
                r.sharedMaterials = copies;
            }

            try
            {
                var icon = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
                if (icon != null && piece.Piece != null) piece.Piece.m_icon = icon;
                else Plugin.Log.LogWarning("[pullerchest] icon render returned nothing; keeping the vanilla icon");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[pullerchest] icon render failed, keeping the vanilla icon: " + e.Message);
            }
        }

        /// <summary>Is this container one of our Puller Chests? Matched on the ZDO's prefab hash, for
        /// the same reason as <see cref="SorterChestPiece.IsSorterChest"/>.</summary>
        internal static bool IsPullerChest(Container c)
        {
            var nv = SorterZdo.NView(c);
            if (nv == null || !nv.IsValid()) return false;
            return nv.GetZDO().GetPrefab() == PrefabHash;
        }
    }
}
