using System;

namespace ChestButler.Core
{
    /// <summary>Per-item routing. Ranking: filter tier (explicit item 3 → group 2 → contains 1),
    /// then sign priority (pN), then WHO HOLDS THE MOST of the item (consolidation),
    /// then distance (candidates arrive nearest-first). Partial fills supported:
    /// returns how much fits; the remainder re-routes to the next-best chest on later ticks.</summary>
    internal static class Router
    {
        internal static Container FindTarget(Container sorter, ItemDrop.ItemData item, float radius, out int amount)
        {
            amount = 0;
            var norm = Names.Normalize(item.m_shared.m_name);
            bool stackable = item.m_shared.m_maxStackSize > 1;

            Container best = null;
            int bestTier = 0;
            int bestPrio = int.MinValue;
            int bestHeld = -1;

            foreach (var c in ContainerTracker.Candidates(sorter, radius))
            {
                var spec = Filters.GetSpec(c);
                if (spec.Ignore || spec.ManualOnly) continue;   // buffers fill via Pull only
                var inv = c.GetInventory();

                int tier = 0;
                if (spec.MatchesItem(norm)) tier = 3;
                else if (spec.MatchesGroup(norm)) tier = 2;
                else if (stackable && Plugin.ContainsFallback.Value &&
                         inv.HaveItem(item.m_shared.m_name, true)) tier = 1;
                if (tier == 0) continue;

                int room = Room(inv, item);
                if (room <= 0) continue;                     // full → next candidate

                int held = inv.CountItems(item.m_shared.m_name, -1, true);

                bool better =
                    tier > bestTier ||
                    (tier == bestTier && (spec.Priority > bestPrio ||
                    (spec.Priority == bestPrio && held > bestHeld)));

                if (better)
                {
                    best = c;
                    bestTier = tier;
                    bestPrio = spec.Priority;
                    bestHeld = held;
                    amount = Math.Min(room, item.m_stack);
                }
            }
            return best;
        }

        /// <summary>How many of this item the inventory can absorb (partial stacks + empty slots).</summary>
        internal static int Room(Inventory inv, ItemDrop.ItemData item)
        {
            int max = item.m_shared.m_maxStackSize;
            if (max <= 1) return inv.GetEmptySlots() > 0 ? 1 : 0;
            return inv.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel)
                   + inv.GetEmptySlots() * max;
        }
    }
}
