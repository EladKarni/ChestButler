using System;
using System.Collections;
using System.Collections.Generic;
using MultiUserChest;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>One resolved transfer against live objects.</summary>
    internal sealed class UnityMove
    {
        public Container Source;
        public ItemDrop.ItemData Item;
        public Container Target;
        public int Amount;
        public string Norm;
    }

    /// <summary>A built Organize plan: the resolved moves plus preview counts.</summary>
    internal sealed class OrganizePlan
    {
        public readonly List<UnityMove> Moves = new List<UnityMove>();
        public OrganizeSummary Summary;
        public bool IsEmpty => Moves.Count == 0;
    }

    /// <summary>Unity glue around the pure OrganizePlanner. Kept deliberately thin and modelled on
    /// SorterBehaviour/Puller, which already work in-game. BuildPlan snapshots live chests into POD
    /// views and delegates ranking to the planner; Execute runs the moves in per-frame batches
    /// through MultiUserChest.</summary>
    internal static class Organizer
    {
        /// <summary>Snapshot every accessible chest in range (origin first, then nearest-first),
        /// run the pure planner, and map its moves back onto live Containers/ItemData.</summary>
        internal static OrganizePlan BuildPlan(Container origin, float radius)
        {
            var result = new OrganizePlan();
            if (origin == null) return result;

            // origin is distance 0 (nearest); Candidates supplies the rest nearest-first.
            var containers = new List<Container> { origin };
            foreach (var c in ContainerTracker.Candidates(origin, radius, excludeSorters: false))
                if (c != origin) containers.Add(c);

            int n = containers.Count;
            var itemLists = new List<ItemDrop.ItemData>[n];
            var views = new List<ChestView>(n);
            // One sample item per norm gives Router.Room the shared-name/stack-size it needs even for
            // a target that holds none of that type yet. First occurrence (nearest) wins.
            var sampleByNorm = new Dictionary<string, ItemDrop.ItemData>();

            for (int i = 0; i < n; i++)
            {
                var c = containers[i];
                var inv = c.GetInventory();
                var spec = Filters.GetSpec(c);
                var stationGroups = Stations.GroupsForChest(c, Plugin.StationRange.Value);   // logs the detected station m_name at Info

                var items = new List<ItemDrop.ItemData>();
                var stacks = new List<StackView>();
                var block = inv != null ? InventoryBlock.Get(inv) : null;

                if (inv != null)
                {
                    foreach (var it in inv.GetAllItems())
                    {
                        if (it == null || it.m_shared == null) continue;
                        if (block != null && block.IsSlotBlocked(it.m_gridPos)) continue; // transfer in flight
                        var norm = Names.Normalize(it.m_shared.m_name);
                        if (norm.Length == 0) continue;
                        items.Add(it);
                        stacks.Add(new StackView
                        {
                            Norm = norm,
                            Count = it.m_stack,
                            Stackable = it.m_shared.m_maxStackSize > 1
                        });
                        if (!sampleByNorm.ContainsKey(norm)) sampleByNorm[norm] = it;
                    }
                }
                itemLists[i] = items;

                bool excluded = SorterZdo.IsSorter(c) || spec.Ignore || spec.ManualOnly;

                // capture-by-value for the closures below
                var specLocal = spec;
                var invLocal = inv;
                var groupsLocal = stationGroups;
                views.Add(new ChestView
                {
                    Id = i,
                    ExcludedAsTarget = excluded,
                    Stacks = stacks,
                    PinsItem = norm => specLocal.MatchesItem(norm),
                    PinsGroup = norm => specLocal.MatchesGroup(norm),
                    StationAttracts = norm => StationAttracts(groupsLocal, norm),
                    Priority = spec.Priority,
                    EmptySlots = inv != null ? inv.GetEmptySlots() : 0,
                    PartialSpaceFor = norm =>
                    {
                        if (invLocal == null) return 0;
                        // NG+ caveat: one sample per norm, so mixed-worldLevel stacks of the same item
                        // make this an estimate; the per-move Room re-check clamps at execute time.
                        return sampleByNorm.TryGetValue(norm, out var sample)
                            ? invLocal.FindFreeStackSpace(sample.m_shared.m_name, sample.m_worldLevel)
                            : 0;
                    },
                    MaxStackOf = norm =>
                        sampleByNorm.TryGetValue(norm, out var sample) ? sample.m_shared.m_maxStackSize : 1
                });
            }

            var planned = OrganizePlanner.Plan(views, out var summary);
            result.Summary = summary;
            foreach (var m in planned)
            {
                result.Moves.Add(new UnityMove
                {
                    Source = containers[m.SrcId],
                    Item = itemLists[m.SrcId][m.SrcStackIndex],
                    Target = containers[m.TgtId],
                    Amount = m.Amount,
                    Norm = m.Norm
                });
            }
            return result;
        }

        private static bool StationAttracts(List<string> groups, string norm)
        {
            if (groups == null) return false;
            for (int i = 0; i < groups.Count; i++)
                if (Groups.GroupContains(groups[i], norm)) return true;
            return false;
        }

        /// <summary>Kick off execution as a coroutine on the plugin so it can spread across frames.
        /// Organize is a client-triggered action (like Pull), NOT owner-gated like the sorter tick.
        /// MultiUserChest routes each REMOVE to the source chest's owner; the destination add is
        /// applied to our local inventory copy, so we claim destination ownership per move (below)
        /// to make the vanilla owner-gated Container.Save persist it.</summary>
        /// <summary>True while a plan is executing. A run spans many frames, and a second plan built
        /// during it cannot see the first one's in-flight moves: the MUC remove path passes
        /// to = (-1,-1), which creates no InventoryBlock, and the destination add only lands on the
        /// RPC response. Two overlapping runs therefore both see the same stack as present and both
        /// issue a remove for it. One run at a time. (1.1.2)</summary>
        private static bool _running;

        internal static bool IsRunning => _running;

        internal static void Execute(OrganizePlan plan)
        {
            if (plan == null || plan.IsEmpty) return;
            if (Plugin.Instance == null)
            {
                Plugin.Log.LogWarning("[organize] no Plugin.Instance; cannot start execution");
                return;
            }
            if (_running)
            {
                Plugin.Log.LogInfo("[organize] a run is already in progress; ignoring this one");
                if (Player.m_localPlayer != null)
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Organize already running");
                return;
            }
            Plugin.Log.LogInfo("[organize] executing " + plan.Moves.Count + " move(s)");
            _running = true;
            Plugin.Instance.StartCoroutine(Run(plan));
        }

        private static IEnumerator Run(OrganizePlan plan)
        {
            try
            {
                int perTick = Mathf.Max(1, Plugin.OrganizeMovesPerTick.Value);
                int budget = perTick;
                int movedItems = 0;
                int skippedMoves = 0, skippedItems = 0;
                var targetsHit = new HashSet<Container>();

                // 1.1.2: MovesPerTick is a per-FRAME budget, so the actual RPC rate scaled with the
                // client's framerate (4/frame is 240/s at 60 fps but 576/s at 144 fps, from one
                // client, at a dedicated server). Hold the nominal 60 fps rate as a ceiling so a
                // high-refresh client cannot flood the server. The config key keeps its meaning;
                // switching it to an explicit per-second unit is a 2.0 change.
                const float NominalFps = 60f;
                float minBatchInterval = 1f / NominalFps;
                float lastBatch = Time.realtimeSinceStartup;

                // 1.1.2: Router.Room reads a LOCAL inventory that does not reflect in-flight adds
                // (MUC applies them on the RPC response), so N moves into the same chest all saw the
                // same free space and all issued — over-committing the target. Debit what we have
                // already promised each destination for the duration of the run.
                var promised = new Dictionary<Container, int>();

                foreach (var mv in plan.Moves)
                {
                    var src = mv.Source;
                    var tgt = mv.Target;
                    var item = mv.Item;
                    if (src == null || tgt == null || item == null || item.m_shared == null) { skippedMoves++; continue; }

                    var sInv = src.GetInventory();
                    var tInv = tgt.GetInventory();
                    if (sInv == null || tInv == null) { skippedMoves++; continue; }
                    if (!sInv.GetAllItems().Contains(item)) { skippedMoves++; skippedItems += mv.Amount; continue; }  // stack gone since preview (stale plan)

                    // 1.1.2: the source was never validated. A chest destroyed or unloaded mid-run
                    // spills its contents as ground drops on its owner's client, and issuing a remove
                    // against that dying ZDO is exactly the window where an item can exist twice.
                    var srcNv = SorterZdo.NView(src);
                    if (srcNv == null || !srcNv.IsValid()) { skippedMoves++; skippedItems += mv.Amount; continue; }

                    var sBlock = InventoryBlock.Get(sInv);
                    if (sBlock != null && sBlock.IsSlotBlocked(item.m_gridPos)) { skippedMoves++; continue; } // in flight

                    int room = Router.Room(tInv, item);                           // re-check: target may have filled
                    if (promised.TryGetValue(tgt, out var already)) room -= already;
                    if (room <= 0) { skippedMoves++; skippedItems += mv.Amount; continue; }
                    int amount = Math.Min(mv.Amount, Math.Min(item.m_stack, room));
                    if (amount <= 0) { skippedMoves++; continue; }

                    var tgtNv = SorterZdo.NView(tgt);
                    if (tgtNv == null || !tgtNv.IsValid()) { skippedMoves++; skippedItems += mv.Amount; continue; }
                    // Another player is browsing it; don't yank ownership. NOTE: Container.m_inUse is a
                    // LOCAL field, so this only sees chests open on THIS client — a remote player's open
                    // chest is invisible to us and the claim below can still strand their edits. A
                    // synced in-use flag is a 2.0 item (every peer runs the mod, so we can add one).
                    if (tgt.IsInUse()) { skippedMoves++; skippedItems += mv.Amount; continue; }

                    // MUC routes the REMOVE to the source owner, but applies the destination add to OUR
                    // local inventory (InventoryHandler.RPC_RequestItemRemoveResponse → MoveItemToThis).
                    // Vanilla Container.Save() is owner-gated, so without owning the destination ZDO the
                    // added items would never persist (silent loss in multiplayer). Same defensive claim
                    // as Filters.SetPinned / SorterZdo.SetSorter.
                    if (!tgtNv.IsOwner()) tgtNv.ClaimOwnership();

                    // exactly Puller's transfer primitive - the ONLY sanctioned write path
                    ContainerHandler.RemoveItemFromChest(
                        src, item, tInv, new Vector2i(-1, -1),
                        tgtNv.GetZDO().m_uid, amount, null);

                    promised[tgt] = already + amount;
                    movedItems += amount;
                    targetsHit.Add(tgt);

                    if (--budget <= 0)
                    {
                        budget = perTick;
                        // Spread across frames so a big base does not hitch, and hold the batch rate
                        // at NominalFps regardless of the client's actual framerate.
                        do { yield return null; }
                        while (Time.realtimeSinceStartup - lastBatch < minBatchInterval);
                        lastBatch = Time.realtimeSinceStartup;
                    }
                }

                string msg = "Organized " + movedItems + " item" + (movedItems == 1 ? "" : "s") +
                             " into " + targetsHit.Count + " chest" + (targetsHit.Count == 1 ? "" : "s");
                if (skippedItems > 0)
                    msg += " (" + skippedItems + " could not move)";

                if (Player.m_localPlayer != null)
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, msg);
                Plugin.Log.LogInfo("[organize] moved " + movedItems + " items into " + targetsHit.Count +
                    " chests; skipped " + skippedMoves + " move(s) covering " + skippedItems + " item(s)");
            }
            finally
            {
                _running = false;
            }
        }
    }
}
