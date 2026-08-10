using System;
using System.Collections.Generic;
using MultiUserChest;

namespace ChestButler.Core
{
    /// <summary>On-demand restock: pulls items matching the destination chest's saved
    /// filters (pins + sign labels) from nearby storage. Per click, each matching item
    /// type moves up to ONE full stack (non-stackables: one item), richest source first.
    /// Pulls from ANY accessible chest in range - manual click is the only trigger.</summary>
    internal static class Puller
    {
        internal static void PullInto(Container dest, out int movedTotal, out int typesMoved)
        {
            movedTotal = 0;
            typesMoved = 0;

            var spec = Filters.GetSpec(dest);
            if (!spec.HasExplicit) return;

            var destInv = dest.GetInventory();
            var destNv = SorterZdo.NView(dest);
            if (destInv == null || destNv == null || !destNv.IsValid()) return;

            // click budget per item type: one stack (or 1 for non-stackables)
            var budget = new Dictionary<string, int>();
            var movedTypes = new HashSet<string>();

            // candidates are distance-sorted; rank per pull by held-count via two passes:
            // gather matching (source, item) pairs, then take from richest sources first.
            var sources = ContainerTracker.Candidates(dest, Plugin.SorterRadius.Value, excludeSorters: false);
            var entries = new List<KeyValuePair<int, KeyValuePair<Container, ItemDrop.ItemData>>>();

            foreach (var src in sources)
            {
                // sort: off means leave this chest entirely alone - Gatherer and Organize already
                // honor that on the source side; Pull was the one reader that did not (audit,
                // 2.0.0 release). Without this, a neighbour's Pull drains a protected chest.
                if (Filters.GetSpec(src).Ignore) continue;

                var sinv = src.GetInventory();
                var sblock = InventoryBlock.Get(sinv);

                foreach (var item in sinv.GetAllItems())
                {
                    if (item == null || item.m_shared == null) continue;
                    if (sblock != null && sblock.IsSlotBlocked(item.m_gridPos)) continue; // transfer in flight
                    var norm = Names.Normalize(item.m_shared.m_name);
                    if (!spec.MatchesItem(norm) && !spec.MatchesGroup(norm)) continue;

                    int held = sinv.CountItems(item.m_shared.m_name, -1, true);
                    entries.Add(new KeyValuePair<int, KeyValuePair<Container, ItemDrop.ItemData>>(
                        held, new KeyValuePair<Container, ItemDrop.ItemData>(src, item)));
                }
            }

            entries.Sort((a, b) => b.Key.CompareTo(a.Key));  // richest first

            foreach (var e in entries)
            {
                var src = e.Value.Key;
                var item = e.Value.Value;
                var norm = Names.Normalize(item.m_shared.m_name);
                int max = item.m_shared.m_maxStackSize;

                if (!budget.TryGetValue(norm, out int left))
                    left = max > 1 ? max : 1;
                if (left <= 0) continue;

                int room = Router.Room(destInv, item);
                if (room <= 0) continue;

                int amount = Math.Min(Math.Min(left, item.m_stack), room);
                if (amount <= 0) continue;

                ContainerHandler.RemoveItemFromChest(
                    src, item, destInv, new Vector2i(-1, -1),
                    destNv.GetZDO().m_uid, amount, null);

                budget[norm] = left - amount;
                movedTotal += amount;
                if (movedTypes.Add(norm)) typesMoved++;
            }
        }
    }
}
