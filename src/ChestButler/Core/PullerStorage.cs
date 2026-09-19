using System;
using System.Collections.Generic;
using MultiUserChest;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>2.1.2 — the Puller Chest's data side: what storage around a Puller holds, and pulling
    /// one item type into it. The panel in <c>PullerPanelPatch</c> only draws and forwards clicks.
    ///
    /// Sources are measured from the Puller Chest rather than from the player, like the chest-toolbar
    /// Pull and unlike Gather: the items land in this chest, so "in range" means in range of it. They
    /// honour every exclusion the rest of the mod does (vehicles, wards, private chests, <c>sort: off</c>,
    /// and through that other Puller Chests). Sorter chests ARE sources: a dump chest is exactly where
    /// something you are looking for might still be sitting.</summary>
    internal static class PullerStorage
    {
        internal sealed class Entry
        {
            internal string SharedName;
            internal string Display;
            internal int Count;
            internal Sprite Icon;
        }

        internal static List<Container> Sources(Container puller)
        {
            var result = new List<Container>();
            if (puller == null) return result;
            foreach (var c in ContainerTracker.Candidates(puller, Plugin.SorterRadius.Value, excludeSorters: false))
            {
                if (Filters.GetSpec(c).Ignore) continue;
                result.Add(c);
            }
            return result;
        }

        /// <summary>Every item type in range with its total count, sorted by display name. One entry
        /// per shared name: quality and world level are not split out, because what the player picks
        /// from the list is "swords", not "the level 2 sword in chest 14".</summary>
        internal static List<Entry> Census(Container puller)
        {
            var byName = new Dictionary<string, Entry>();
            foreach (var src in Sources(puller))
            {
                var inv = src.GetInventory();
                if (inv == null) continue;

                foreach (var item in inv.GetAllItems())
                {
                    var name = item?.m_shared?.m_name;
                    if (string.IsNullOrEmpty(name)) continue;

                    if (!byName.TryGetValue(name, out var e))
                    {
                        e = new Entry
                        {
                            SharedName = name,
                            Display = Localization.instance != null ? Localization.instance.Localize(name) : name,
                            Icon = item.GetIcon(),
                        };
                        byName.Add(name, e);
                    }
                    e.Count += item.m_stack;
                }
            }

            var list = new List<Entry>(byName.Values);
            list.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Pull one item type into <paramref name="dest"/>: one full stack (one item for
        /// non-stackables), or everything in range that fits when <paramref name="all"/> is set. Richest
        /// chest first, so a pull empties the obvious pile rather than scraping one item from each of a
        /// dozen chests. Returns how many items were issued.</summary>
        internal static int Pull(Container dest, string sharedName, bool all)
        {
            if (dest == null || string.IsNullOrEmpty(sharedName)) return 0;
            var destInv = dest.GetInventory();
            var destNv = SorterZdo.NView(dest);
            if (destInv == null || destNv == null || !destNv.IsValid()) return 0;

            var ranked = new List<KeyValuePair<int, Container>>();
            foreach (var src in Sources(dest))
            {
                var inv = src.GetInventory();
                if (inv == null) continue;
                int held = inv.CountItems(sharedName, -1, true);
                if (held > 0) ranked.Add(new KeyValuePair<int, Container>(held, src));
            }
            ranked.Sort((a, b) =>
            {
                int d = b.Key.CompareTo(a.Key);
                return d != 0 ? d : ContainerTracker.CompareUid(a.Value, b.Value);
            });

            int want = -1;          // set from the first matching stack's max stack size
            int moved = 0;

            // Same over-commit hazard as Gather: a transfer out of a chest another peer owns is an RPC,
            // so the destination does not show it yet and every later room check would pass. Everything
            // promised in this call is debited up front. One item type per call, so a single running
            // total covers both stack space and, for non-stackables, slots.
            int promised = 0;

            foreach (var entry in ranked)
            {
                var src = entry.Value;
                var sInv = src.GetInventory();
                if (sInv == null) continue;
                var block = InventoryBlock.Get(sInv);

                // Snapshot: the transfer mutates the source inventory.
                foreach (var item in new List<ItemDrop.ItemData>(sInv.GetAllItems()))
                {
                    if (item?.m_shared == null || item.m_shared.m_name != sharedName) continue;
                    if (block != null && block.IsSlotBlocked(item.m_gridPos)) continue;   // already in flight

                    int max = Math.Max(1, item.m_shared.m_maxStackSize);
                    if (want < 0) want = all ? int.MaxValue : max;

                    int room = RoomFor(destInv, item, max) - promised;
                    if (room <= 0) return moved;                          // Puller Chest is full

                    int amount = Math.Min(Math.Min(want, item.m_stack), room);
                    if (amount <= 0) continue;

                    ContainerHandler.RemoveItemFromChest(
                        src, item, destInv, new Vector2i(-1, -1),
                        destNv.GetZDO().m_uid, amount, null);

                    moved += amount;
                    promised += amount;
                    want -= amount;
                    if (want <= 0) return moved;
                }
            }
            return moved;
        }

        /// <summary>Capped per call. Each returned stack costs a full Router.FindTarget sweep of the
        /// base, so a Puller used as a second wardrobe would otherwise pay for 24 of them in one frame.
        /// The timer picks up the rest on its next tick and the button can be pressed again.</summary>
        private const int MaxReturnsPerCall = 16;

        /// <summary>Send what is in the Puller back to storage, routing every stack exactly as the
        /// sorter would (<see cref="Router.FindTarget"/>), so items land in the chest that holds them,
        /// in a chest a pin or sign claims, or in a free chest. Driven by the panel's Send back button
        /// and by <see cref="PullerBehaviour"/>'s timer. Returns how many items were issued.</summary>
        internal static int ReturnAll(Container puller, out int types)
        {
            types = 0;
            int moved = 0;
            if (puller == null) return 0;

            var inv = puller.GetInventory();
            var nv = SorterZdo.NView(puller);
            if (inv == null || nv == null || !nv.IsValid()) return 0;

            // Rule zero, exactly as in SorterBehaviour and Organizer: only the ZDO owner moves items
            // OUT of a chest. MUC's AddItemToChest removes from the source inventory locally and
            // unconditionally, and a non-owner's edit is never saved (Container.Save is owner-gated),
            // so the next Load brings the items back while the target keeps its copy. That is a
            // duplication, and it is reachable: MUC lets a second player hold this chest open without
            // owning it, and the Send back button is theirs to press.
            long owner = nv.GetZDO().GetOwner();
            if (owner != 0L && !nv.IsOwner()) return 0;
            if (!nv.IsOwner()) nv.ClaimOwnership();

            // An Organize run has already resolved where every stack in range belongs and is issuing
            // moves against that plan. Returning items into it mid-run is how a plan ends up reporting
            // skipped moves it cannot explain.
            if (Organizer.IsRunning) return 0;

            var block = InventoryBlock.Get(inv);
            var claimedEmpty = new HashSet<Container>();
            var promised = new Dictionary<Container, int>();
            var seen = new HashSet<string>();
            int issued = 0;

            // The throttle only knows what it is allowed to measure, and this is a base-wide sweep per
            // stack, the same shape of work as the sorter tick and an Organize run.
            using (Throttle.Measure())
            {
                // Snapshot: an inline transfer mutates this inventory while we walk it.
                foreach (var item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                {
                    if (issued >= MaxReturnsPerCall) break;
                    if (item?.m_shared == null) continue;
                    if (block != null && block.IsSlotBlocked(item.m_gridPos)) continue;   // already in flight

                    var target = Router.FindTarget(puller, item, Plugin.SorterRadius.Value, out int amount, claimedEmpty);
                    if (target == null || amount <= 0) continue;

                    // Router.Room reads a LOCAL inventory, which does not reflect what this same call
                    // already sent to that chest over RPC. Without this, ten stacks all pass the room
                    // check for one chest's worth of space (v2 plan §16.2.3).
                    int already = promised.TryGetValue(target, out int p) ? p : 0;
                    amount -= already;
                    if (amount <= 0) continue;

                    // Somebody has that chest open on this client: their deposit and our push would
                    // race, so leave it for the next call (v2 plan §16.2.2).
                    if (target.IsInUse()) continue;

                    var tgtNv = SorterZdo.NView(target);
                    if (tgtNv == null || !tgtNv.IsValid()) continue;

                    // §16.2.9: MUC silently declines an add to a chest no peer owns, which is a normal
                    // state for a chest in the outer activeArea band. Claim it the way Organize does.
                    if (!tgtNv.HasOwner()) tgtNv.ClaimOwnership();

                    var request = ContainerHandler.AddItemToChest(
                        target, item, inv, new Vector2i(-1, -1),
                        nv.GetZDO().m_uid, amount);

                    // A non-null request with no RequestID is MUC's dummy decline: nothing was removed
                    // and nothing was sent. Counting those as moved is what gave Organize phantom
                    // successes, and here it would tell the timer it made progress forever.
                    if (request != null && request.RequestID == 0) continue;

                    promised[target] = already + amount;
                    moved += amount;
                    issued++;
                    if (seen.Add(Names.Normalize(item.m_shared.m_name))) types++;
                }
            }

            return moved;
        }

        private static int RoomFor(Inventory inv, ItemDrop.ItemData item, int max)
        {
            if (max <= 1) return inv.GetEmptySlots();                    // one slot per item
            return inv.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel) + inv.GetEmptySlots() * max;
        }
    }
}
