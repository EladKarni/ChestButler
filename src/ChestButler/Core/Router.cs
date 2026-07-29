using System;
using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>Per-item routing. Ranking: filter tier (explicit item 4 → group/sign 3 → station
    /// adjacency 2 → contains 1), then sign priority (pN), then WHO HOLDS THE MOST of the item
    /// (consolidation), then distance (candidates arrive nearest-first). Partial fills supported:
    /// returns how much fits; the remainder re-routes to the next-best chest on later ticks.
    ///
    /// W1 (v2 plan §15.8): the STATION tier is new. <c>OrganizePlanner</c>'s doc claimed it mirrored
    /// this ranking "exactly so Organize and the live sorter never disagree" — it did not: the planner
    /// had a station tier and this had none. The result was a livelock in the shipped code. Chest A
    /// sits by the forge with no pin; chest B far away holds 40 iron. The sorter pushed iron to B
    /// (tier 1; A did not qualify at all), Organize saw Station beat Holds and moved it B → A, more
    /// iron arrived and went back to B — forever. With the tier here, both loops agree.</summary>
    internal static class Router
    {
        /// <summary>How long a cached station lookup is trusted on the tick path. Stations do not move;
        /// a newly built one starts attracting items at most this late.</summary>
        private const float StationCacheTtl = 10f;

        internal static Container FindTarget(Container sorter, ItemDrop.ItemData item, float radius, out int amount)
        {
            amount = 0;
            var norm = Names.Normalize(item.m_shared.m_name);
            bool stackable = item.m_shared.m_maxStackSize > 1;

            var sorterPos = sorter.transform.position;
            float stationRange = Plugin.StationRange.Value;

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
                if (spec.MatchesItem(norm)) tier = 4;
                else if (spec.MatchesGroup(norm)) tier = 3;
                else if (StationAttracts(c, sorterPos, radius, stationRange, norm)) tier = 2;
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

        /// <summary>Does a crafting station next to this chest attract the item's group? Uses the shared
        /// cached station pass — a per-chest scan here would land once per candidate per item per tick,
        /// which is exactly the cost §16.3 flags as the path that breaks the game at scale.</summary>
        private static bool StationAttracts(Container c, UnityEngine.Vector3 center, float radius,
                                            float stationRange, string norm)
        {
            var groups = Stations.GroupsForChestCached(c, center, radius + stationRange,
                                                      stationRange, StationCacheTtl);
            if (groups == null || groups.Count == 0) return false;
            for (int i = 0; i < groups.Count; i++)
                if (Groups.GroupContains(groups[i], norm)) return true;
            return false;
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
