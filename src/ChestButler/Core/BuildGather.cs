using System.Collections.Generic;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>2.1.1 — Gather for building. The crafting button answers "fetch what this recipe is
    /// missing"; this answers the same question for the piece the player is trying to place.
    ///
    /// Everything downstream is shared: the shortfall arithmetic is <see cref="GatherMath"/> and the
    /// transfer is <see cref="Gatherer.Pull"/>, so build gathering reaches chests through exactly the
    /// same MultiUserChest path as crafting. Only the question differs, a <see cref="Piece"/>'s
    /// <c>m_resources</c> instead of a recipe's requirement rows.</summary>
    internal static class BuildGather
    {
        /// <summary>Hud.UpdateBuild runs every frame while the hammer is out, and Gatherer.Sources()
        /// walks every tracked container. The crafting panel re-queries once per refresh, which is
        /// right there and wrong here: per frame it would be a scan per frame, and cached once it
        /// would never notice a chest being filled. One second is short enough that the count is
        /// honest and long enough that the scan is not the cost of holding a hammer.</summary>
        private const float SourceMaxAge = 1f;

        private static List<Container> _sources;
        private static float _sourcesAt = float.NegativeInfinity;

        internal static List<Container> SourcesCached()
        {
            float now = Time.unscaledTime;
            if (_sources == null || now - _sourcesAt > SourceMaxAge)
            {
                _sources = Gatherer.Sources();
                _sourcesAt = now;
            }
            return _sources;
        }

        /// <summary>Drop the cache after a pull: the chests we just emptied are the ones the next
        /// count would otherwise still report as full.</summary>
        internal static void Invalidate() => _sources = null;

        /// <summary>What placing one <paramref name="piece"/> needs. Build costs are flat, so this
        /// reads <c>m_amount</c> rather than <c>GetAmount(quality)</c>: quality scaling is a crafting
        /// and upgrade concept, and a piece has no quality to pass.</summary>
        internal static List<GatherNeed> NeedsFor(Piece piece)
        {
            var needs = new List<GatherNeed>();

            var player = Player.m_localPlayer;
            if (piece == null || player == null || piece.m_resources == null) return needs;

            var inv = player.GetInventory();
            var sources = SourcesCached();

            foreach (var req in piece.m_resources)
            {
                var shared = req?.m_resItem?.m_itemData?.m_shared;
                if (shared == null || string.IsNullOrEmpty(shared.m_name)) continue;
                if (req.m_amount <= 0) continue;

                needs.Add(new GatherNeed
                {
                    SharedName = shared.m_name,
                    Display = Names.Normalize(shared.m_name),
                    Needed = req.m_amount,
                    InPlayer = inv != null ? inv.CountItems(shared.m_name, -1, true) : 0,
                    InStorage = Gatherer.CountInStorage(sources, shared.m_name),
                });
            }

            return needs;
        }
    }
}
