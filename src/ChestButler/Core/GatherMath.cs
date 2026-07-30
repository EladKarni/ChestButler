using System;
using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>One recipe ingredient, resolved against the player's inventory and nearby storage.</summary>
    internal struct GatherNeed
    {
        public string SharedName;   // ItemDrop.ItemData.SharedData.m_name, for CountItems
        public string Display;      // normalized name, for logs
        public int Needed;          // GetAmount(quality) * craftMultiplier
        public int InPlayer;
        public int InStorage;

        public int Shortfall => Math.Max(0, Needed - InPlayer);

        /// <summary>How much of the shortfall storage can actually cover.</summary>
        public int Gatherable => Math.Min(Shortfall, InStorage);
    }

    /// <summary>Gather's shortfall arithmetic, kept Unity-free and config-free so the offline suite can
    /// reach it. The UI and the MultiUserChest seam cannot be tested without a game; this is the part
    /// where the off-by-one bugs actually live, so it is split out deliberately (same reasoning as
    /// <see cref="BucketKeys"/> and <see cref="GroupTables"/>).</summary>
    internal static class GatherMath
    {
        /// <summary>Fold per-ingredient counts into the list worth fetching.
        /// <paramref name="onlyOneIngredient"/> is <c>Recipe.m_requireOnlyOneIngredient</c>: those
        /// recipes consume ONE of the listed options, so pulling for all of them would haul in several
        /// stacks the player will never spend.</summary>
        internal static List<GatherNeed> Resolve(IList<GatherNeed> raw, bool onlyOneIngredient)
        {
            var result = new List<GatherNeed>();
            if (raw == null || raw.Count == 0) return result;

            if (!onlyOneIngredient)
            {
                for (int i = 0; i < raw.Count; i++)
                    if (raw[i].Gatherable > 0) result.Add(raw[i]);
                return result;
            }

            int pick = PickSingleIngredient(raw);
            if (pick >= 0 && raw[pick].Gatherable > 0) result.Add(raw[pick]);
            return result;
        }

        /// <summary>For a require-only-one recipe, the ingredient worth fetching: the one storage brings
        /// closest to satisfying, then the smallest remaining shortfall, then name order so the choice is
        /// stable between openings. -1 when the player already has an option covered, or nothing is
        /// available.</summary>
        internal static int PickSingleIngredient(IList<GatherNeed> raw)
        {
            // One option already satisfied by what the player carries → fetch nothing at all.
            for (int i = 0; i < raw.Count; i++)
                if (raw[i].Shortfall == 0) return -1;

            int best = -1;
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i].Gatherable <= 0) continue;
                if (best < 0) { best = i; continue; }

                int remainingI = raw[i].Shortfall - raw[i].Gatherable;
                int remainingB = raw[best].Shortfall - raw[best].Gatherable;
                if (remainingI < remainingB) { best = i; continue; }
                if (remainingI > remainingB) continue;

                if (raw[i].Shortfall < raw[best].Shortfall) { best = i; continue; }
                if (raw[i].Shortfall > raw[best].Shortfall) continue;

                if (string.CompareOrdinal(raw[i].SharedName ?? "", raw[best].SharedName ?? "") < 0)
                    best = i;
            }
            return best;
        }
    }
}
