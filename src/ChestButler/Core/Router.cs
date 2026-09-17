using System;
using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>Per-item routing. Ranking: filter tier (explicit item 4 → group/sign 3 → station
    /// adjacency 2 → contains 1), then sign priority (pN), then WHO HOLDS THE MOST of the item
    /// (consolidation), then distance (candidates arrive nearest-first). Partial fills supported:
    /// returns how much fits; the remainder re-routes to the next-best chest on later ticks.
    ///
    /// 2.1.2: when no tier matches, the item no longer just sits in the sorter. It goes to the nearest
    /// EMPTY chest, and when there is none, to the chest with the most free slots
    /// (<c>[Sorting] FreeChestFallback</c>). Before this a base full of empty chests could leave a
    /// sorter doing nothing at all, because a type nobody held had nowhere to go. Once it lands, the
    /// contains tier picks up every later stack of that type, so the fallback only ever decides where
    /// a NEW type starts. Chests that already mean something are never picked by it: a pin or sign
    /// label, a station next to the chest, and for the empty-chest step an Organize home too, since an
    /// empty home is reserved for its bucket. The contains tier also routes non-stackables now, or
    /// every sword would claim an empty chest of its own.
    ///
    /// W1 (v2 plan §15.8): the STATION tier is new. <c>OrganizePlanner</c>'s doc claimed it mirrored
    /// this ranking "exactly so Organize and the live sorter never disagree" — it did not: the planner
    /// had a station tier and this had none. The result was a livelock in the shipped code. Chest A
    /// sits by the forge with no pin; chest B far away holds 40 iron. The sorter pushed iron to B
    /// (tier 1; A did not qualify at all), Organize saw Station beat Holds and moved it B → A, more
    /// iron arrived and went back to B — forever. With the tier here, both loops agree. The free-chest
    /// fallback cannot start one of those: it only fires when nothing holds the type, and whatever
    /// Organize later does with it, the contains tier follows.</summary>
    internal static class Router
    {
        /// <summary>How long a cached station lookup is trusted on the tick path. Stations do not move;
        /// a newly built one starts attracting items at most this late.</summary>
        private const float StationCacheTtl = 10f;

        /// <param name="claimedEmpty">Empty chests already handed out earlier in the same sorter tick.
        /// A transfer into a chest another peer owns is an RPC, so that chest still reads empty locally
        /// and two new types in one tick would otherwise both start in it. Null to skip the check.</param>
        internal static Container FindTarget(Container sorter, ItemDrop.ItemData item, float radius, out int amount,
                                             ICollection<Container> claimedEmpty = null)
        {
            amount = 0;
            var norm = Names.Normalize(item.m_shared.m_name);

            var sorterPos = sorter.transform.position;
            float stationRange = Plugin.StationRange.Value;
            bool freeFallback = Plugin.FreeChestFallback == null || Plugin.FreeChestFallback.Value;

            Container best = null;
            int bestTier = 0;
            int bestPrio = int.MinValue;
            int bestHeld = -1;

            Container empty = null;          // nearest eligible empty chest
            int emptyRoom = 0;
            Container roomiest = null;       // eligible chest with the most free slots, nearest on ties
            int roomiestSlots = 0;
            int roomiestRoom = 0;

            foreach (var c in ContainerTracker.Candidates(sorter, radius))
            {
                var spec = Filters.GetSpec(c);
                if (spec.Ignore || spec.ManualOnly) continue;   // buffers fill via Pull only
                var inv = c.GetInventory();

                int tier = 0;
                bool hasStation = false;
                if (spec.MatchesItem(norm)) tier = 4;
                else if (spec.MatchesGroup(norm)) tier = 3;
                else
                {
                    var stationGroups = Stations.GroupsForChestCached(c, sorterPos, radius + stationRange,
                                                                      stationRange, StationCacheTtl);
                    hasStation = stationGroups != null && stationGroups.Count > 0;
                    if (hasStation && StationAttracts(stationGroups, norm)) tier = 2;
                    else if (Plugin.ContainsFallback.Value && inv.HaveItem(item.m_shared.m_name, true)) tier = 1;
                }

                // Fallback candidates are only collected while no tier match exists; a tier match
                // found later in the loop still wins over them.
                if (tier == 0 && (!freeFallback || best != null || spec.HasExplicit || hasStation)) continue;

                int room = Room(inv, item);
                if (room <= 0) continue;                     // full → next candidate

                if (tier == 0)
                {
                    if (empty == null && inv.GetAllItems().Count == 0 && spec.Home == null &&
                        (claimedEmpty == null || !claimedEmpty.Contains(c)))
                    {
                        empty = c;
                        emptyRoom = room;
                    }

                    int slots = inv.GetEmptySlots();
                    if (slots > roomiestSlots)
                    {
                        roomiest = c;
                        roomiestSlots = slots;
                        roomiestRoom = room;
                    }
                    continue;
                }

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

            if (best != null) return best;

            if (empty != null)
            {
                amount = Math.Min(emptyRoom, item.m_stack);
                claimedEmpty?.Add(empty);
                return empty;
            }

            if (roomiest != null)
            {
                amount = Math.Min(roomiestRoom, item.m_stack);
                return roomiest;
            }

            return null;
        }

        /// <summary>Does a crafting station next to this chest attract the item's group? The groups
        /// come from the shared cached station pass — a per-chest scan here would land once per
        /// candidate per item per tick, which is exactly the cost §16.3 flags as the path that breaks
        /// the game at scale.</summary>
        private static bool StationAttracts(List<string> groups, string norm)
        {
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
