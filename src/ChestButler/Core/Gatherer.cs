using System;
using System.Collections.Generic;
using MultiUserChest;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Gather's live half: which chests are reachable, what they hold, and the actual
    /// MultiUserChest pull. The shortfall arithmetic lives in <see cref="GatherMath"/>, which is
    /// Unity-free and unit-tested offline.</summary>
    internal static class Gatherer
    {
        // ---- live queries -------------------------------------------------------------------------

        /// <summary>Accessible chests around the player, honouring the same exclusions as everything
        /// else. `sort: off` chests are skipped: W1 made that label mean "leave this chest entirely
        /// alone", and a Gather button that quietly emptied a personal stash would reintroduce v2 plan
        /// §16.4.5 through the back door. Manual chests ARE read — Manual means "never auto-FILLED",
        /// and Gather is an explicit click, exactly like Pull.</summary>
        internal static List<Container> Sources()
        {
            var result = new List<Container>();
            var player = Player.m_localPlayer;
            if (player == null) return result;

            foreach (var c in ContainerTracker.AccessibleNear(player.transform.position,
                                                              Plugin.SorterRadius.Value))
            {
                if (Filters.GetSpec(c).Ignore) continue;
                result.Add(c);
            }
            return result;
        }

        internal static int CountInStorage(List<Container> sources, string sharedName)
        {
            if (sources == null || string.IsNullOrEmpty(sharedName)) return 0;
            int total = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                var inv = sources[i].GetInventory();
                if (inv == null) continue;
                total += inv.CountItems(sharedName, -1, true);
            }
            return total;
        }

        /// <summary>Pull the resolved needs out of storage into the player's inventory. Returns how many
        /// items were issued and how many distinct types they covered.</summary>
        internal static void Pull(List<GatherNeed> needs, out int movedTotal, out int typesMoved)
        {
            movedTotal = 0;
            typesMoved = 0;

            var player = Player.m_localPlayer;
            if (player == null || needs == null || needs.Count == 0) return;

            var destInv = player.GetInventory();
            if (destInv == null) return;

            // The RPC response is routed back to the SENDER, and the destination is passed as a live
            // Inventory — so a player ZDOID is exactly the right sender here. (Verified against
            // MultiUserChest.dll; see docs/gather-plan.md §2.3.)
            var sender = player.GetZDOID();

            var sources = Sources();

            // Same over-commit hazard as Organize (v2 plan §16.2.3): Router.Room reads a local
            // inventory that does not reflect in-flight adds, so without debiting at issue time N pulls
            // into a nearly-full inventory all pass the room check and over-fill it.
            int promised = 0;

            foreach (var need in needs)
            {
                int want = need.Gatherable;
                if (want <= 0) continue;
                bool movedAny = false;

                // Richest chest first, so a craft is satisfied from the obvious pile rather than
                // scraping a dozen chests for one item each.
                var ranked = new List<KeyValuePair<int, Container>>();
                for (int i = 0; i < sources.Count; i++)
                {
                    var inv = sources[i].GetInventory();
                    if (inv == null) continue;
                    int held = inv.CountItems(need.SharedName, -1, true);
                    if (held > 0) ranked.Add(new KeyValuePair<int, Container>(held, sources[i]));
                }
                ranked.Sort((a, b) =>
                {
                    int d = b.Key.CompareTo(a.Key);
                    return d != 0 ? d : ContainerTracker.CompareUid(a.Value, b.Value);
                });

                foreach (var entry in ranked)
                {
                    if (want <= 0) break;
                    var src = entry.Value;
                    var sInv = src.GetInventory();
                    if (sInv == null) continue;

                    var srcNv = SorterZdo.NView(src);
                    if (srcNv == null || !srcNv.IsValid()) continue;
                    if (!SorterZdo.PlayerCanAccess(src)) continue;
                    if (!PrivateArea.CheckAccess(src.transform.position, 0f, false, true)) continue;

                    var block = InventoryBlock.Get(sInv);

                    // Snapshot: the transfer mutates the source inventory on the RPC response.
                    var stacks = new List<ItemDrop.ItemData>(sInv.GetAllItems());
                    foreach (var item in stacks)
                    {
                        if (want <= 0) break;
                        if (item?.m_shared == null) continue;
                        if (item.m_shared.m_name != need.SharedName) continue;
                        if (block != null && block.IsSlotBlocked(item.m_gridPos)) continue;

                        int room = Router.Room(destInv, item) - promised;
                        if (room <= 0) { want = 0; break; }       // player is full; stop this type

                        int amount = Math.Min(Math.Min(want, item.m_stack), room);
                        if (amount <= 0) continue;

                        // Exactly Puller's primitive — the ONLY sanctioned write path.
                        ContainerHandler.RemoveItemFromChest(
                            src, item, destInv, new Vector2i(-1, -1),
                            sender, amount, null);

                        promised += amount;
                        movedTotal += amount;
                        want -= amount;
                        movedAny = true;
                    }
                }

                if (movedAny) typesMoved++;
            }

            if (movedTotal > 0)
                Plugin.Log.LogInfo("[gather] pulled " + movedTotal + " item(s) across " + typesMoved + " type(s)");
        }
    }
}
