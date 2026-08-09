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
        public string Bucket;
    }

    /// <summary>A built Organize plan: the resolved moves, the psort_home markers to write, and the
    /// preview counts. Public shape is APPEND-ONLY — Patches/GuiPatch.cs compiles against it
    /// (roadmap §3).</summary>
    internal sealed class OrganizePlan
    {
        public readonly List<UnityMove> Moves = new List<UnityMove>();

        /// <summary>Chests to mark (or unmark) as a bucket's claimed home. v2 plan §4.1.</summary>
        public readonly List<KeyValuePair<Container, string>> HomeMarks =
            new List<KeyValuePair<Container, string>>();

        public OrganizeSummary Summary;
        public bool IsEmpty => Moves.Count == 0;

        /// <summary>Plan-time cost, so the DoD's "stopwatch BuildPlan before and after" is a measured
        /// number in the log rather than an assumption.</summary>
        public float BuildMs;
    }

    /// <summary>Unity glue around the pure allocator. BuildPlan snapshots live chests into POD views,
    /// classifies every item into exactly one bucket, and delegates allocation to
    /// <see cref="OrganizePlanner"/>; Execute runs the resulting moves through MultiUserChest with a
    /// retry queue, a real per-second rate and live re-validation before every single transfer.</summary>
    internal static class Organizer
    {
        // ---- execution tuning ---------------------------------------------------------------------

        /// <summary>§16.2.5: the retry queue needs a termination rule. Two attempts per move, and the
        /// whole queue is abandoned as soon as a full drain issues nothing — which is what stops the
        /// three non-terminating generators (NG+ mixed-worldLevel stacks that can never merge, a
        /// two-chest eviction cycle with no spare slot, and a target a player leaves open).</summary>
        private const int MaxAttemptsPerMove = 2;

        /// <summary>§16.3: cap outstanding MUC requests and give each a deadline. MUC's own
        /// InventoryBlock cannot be used for this — our to = (-1,-1) calls never create a block, and a
        /// dropped response would block a slot permanently (§15.6) — so we keep our own ledger off the
        /// request handle that ContainerHandler returns.</summary>
        private const int MaxOutstanding = 8;
        private const float OutstandingDeadline = 10f;

        // ---- plan ---------------------------------------------------------------------------------

        /// <summary>Snapshot every accessible chest in range, classify its contents, and run the
        /// allocator.</summary>
        internal static OrganizePlan BuildPlan(Container origin, float radius)
        {
            var result = new OrganizePlan();
            if (origin == null) return result;

            var timer = Throttle.Measure();
            try
            {
                BuildPlanInner(origin, radius, result);
            }
            finally
            {
                result.BuildMs = timer.Milliseconds;
                timer.Dispose();            // feeds the self-throttle (§16.6)
                Plugin.Log.LogInfo("[organize] BuildPlan took " + result.BuildMs.ToString("0.0") +
                    " ms: " + result.Moves.Count + " move(s), " + result.Summary.BucketsPlanned +
                    " bucket(s), " + result.Summary.HomelessItems + " item(s) with no room");
            }
            return result;
        }

        private static void BuildPlanInner(Container origin, float radius, OrganizePlan result)
        {
            // origin is distance 0; CandidatesWithDistance supplies the rest by (distance, ZDO uid).
            var containers = new List<Container> { origin };
            var distances = new List<float> { 0f };
            foreach (var cd in ContainerTracker.CandidatesWithDistance(origin, radius, excludeSorters: false))
            {
                if (cd.Chest == origin) continue;
                containers.Add(cd.Chest);
                distances.Add(cd.Distance);
            }

            int n = containers.Count;

            // §15.2 / §16.3: ONE station pass per run. GroupsForChest walks every CraftingStation and
            // every registered processor with a GetComponentInParent<ZNetView>() per candidate, so
            // calling it per chest was the single most expensive thing Organize did — tens of thousands
            // of native hierarchy walks per plan.
            var stationGroups = BuildStationIndex(containers, Plugin.StationRange.Value);

            var itemLists = new List<ItemDrop.ItemData>[n];
            var specs = new FilterSpec[n];
            var invs = new Inventory[n];
            var stacksPerChest = new List<StackView>[n];
            var positions = new Vector3[n];

            // One sample per ITEM TYPE, for classification.
            var sampleByNorm = new Dictionary<string, ItemDrop.ItemData>(StringComparer.Ordinal);

            // One sample per MERGE KEY, for capacity. Two stacks of the same item at different NG+
            // world levels can never merge into one slot, so the allocator must not treat them as one
            // pile: budgeting ceil(total / maxStack) slots for them under-counts, the extra moves can
            // never complete, and they re-queue forever (v2 plan §16.2.5, §16.5a). Making the merge key
            // part of the stack identity makes the slot maths exact instead of optimistic.
            var sampleByKey = new Dictionary<string, ItemDrop.ItemData>(StringComparer.Ordinal);
            var baseOfKey = new Dictionary<string, string>(StringComparer.Ordinal);

            // ---- census -------------------------------------------------------------------------
            for (int i = 0; i < n; i++)
            {
                var c = containers[i];
                positions[i] = c.transform.position;
                var inv = c.GetInventory();
                invs[i] = inv;
                specs[i] = Filters.GetSpec(c);

                var items = new List<ItemDrop.ItemData>();
                var stacks = new List<StackView>();
                var block = inv != null ? InventoryBlock.Get(inv) : null;

                if (inv != null)
                {
                    foreach (var it in inv.GetAllItems())
                    {
                        if (it == null || it.m_shared == null) continue;
                        // Left in place deliberately: whether our to=(-1,-1) calls ever create a block
                        // is unresolved pending an in-game check (roadmap §9 item 3), and removing a
                        // guard on an unverified assumption is the wrong risk.
                        if (block != null && block.IsSlotBlocked(it.m_gridPos)) continue;

                        var norm = Names.Normalize(it.m_shared.m_name);
                        if (norm.Length == 0) continue;

                        // Stacks that cannot merge get distinct identities (see sampleByKey above).
                        string key = it.m_worldLevel > 0 ? norm + "#" + it.m_worldLevel : norm;

                        items.Add(it);
                        stacks.Add(new StackView
                        {
                            Norm = key,
                            Count = it.m_stack,
                            Stackable = it.m_shared.m_maxStackSize > 1,
                            BucketKey = null,           // filled in once every item type is known
                        });
                        if (!sampleByNorm.ContainsKey(norm)) sampleByNorm[norm] = it;
                        if (!sampleByKey.ContainsKey(key)) { sampleByKey[key] = it; baseOfKey[key] = norm; }
                    }
                }
                itemLists[i] = items;
                stacksPerChest[i] = stacks;
            }

            // ---- classify (v2 plan §5) ------------------------------------------------------------
            bool includeGear = OrganizeConfig.IncludeGear == null || OrganizeConfig.IncludeGear.Value;
            var bucketOfNorm = ClassifyNorms(containers, specs, sampleByNorm, includeGear);

            for (int i = 0; i < n; i++)
            {
                var stacks = stacksPerChest[i];
                for (int s = 0; s < stacks.Count; s++)
                {
                    var sv = stacks[s];
                    // Bucket comes from the ITEM TYPE; the world level only affects mergeability, and
                    // must never send the same item to two different buckets.
                    var baseNorm = baseOfKey.TryGetValue(sv.Norm, out var bn) ? bn : sv.Norm;
                    sv.BucketKey = bucketOfNorm.TryGetValue(baseNorm, out var b) ? b : null;
                    stacks[s] = sv;
                }
            }

            // ---- views ---------------------------------------------------------------------------
            var views = new List<ChestView>(n);
            for (int i = 0; i < n; i++)
            {
                var c = containers[i];
                var spec = specs[i];
                var inv = invs[i];

                // v2 plan §4 exclusion table. `sort: off` is the true opt-out: not a target AND not a
                // source. Manual keeps its existing "never auto-filled, Organize may still take from
                // it" meaning, and a Sorter chest stays a source because draining it IS the feature.
                bool excludedTarget = SorterZdo.IsSorter(c) || spec.Ignore || spec.ManualOnly;
                bool excludedSource = spec.Ignore;

                var nv = SorterZdo.NView(c);
                string uid = nv != null && nv.IsValid() ? nv.GetZDO().m_uid.ToString() : "";

                views.Add(new ChestView
                {
                    Id = i,
                    UidKey = uid,
                    Distance = distances[i],
                    TotalSlots = inv != null ? Math.Max(0, inv.GetWidth() * inv.GetHeight()) : 0,
                    Priority = spec.Priority,
                    Stacks = stacksPerChest[i],
                    ExcludedAsTarget = excludedTarget,
                    ExcludedAsSource = excludedSource,
                    HomeMarker = spec.Home,
                    Anchors = BuildAnchors(spec, stationGroups[i], bucketOfNorm, excludedTarget),
                });
            }

            // ---- allocate ------------------------------------------------------------------------
            var groupRank = BuildBucketRanker();
            var input = new PlannerInput
            {
                Chests = views,
                MaxStackOf = key => sampleByKey.TryGetValue(key, out var s)
                    ? Math.Max(1, s.m_shared.m_maxStackSize) : 1,
                BucketRank = groupRank,
                DistanceBetween = (a, b) => Vector3.Distance(positions[a], positions[b]),
                MiscPromoteSlots = OrganizeConfig.MiscPromoteSlots != null
                    ? OrganizeConfig.MiscPromoteSlots.Value : 24,
            };

            var planned = OrganizePlanner.Plan(input);
            result.Summary = planned.Summary;

            foreach (var m in planned.Moves)
            {
                var srcItems = itemLists[m.SrcId];
                if (m.SrcStackIndex < 0 || m.SrcStackIndex >= srcItems.Count) continue;
                var baseNorm = baseOfKey.TryGetValue(m.Norm, out var bn) ? bn : m.Norm;
                result.Moves.Add(new UnityMove
                {
                    Source = containers[m.SrcId],
                    Item = srcItems[m.SrcStackIndex],
                    Target = containers[m.TgtId],
                    Amount = m.Amount,
                    Norm = baseNorm,
                    Bucket = bucketOfNorm.TryGetValue(baseNorm, out var bk) ? bk : null,
                });
            }

            foreach (var hm in planned.HomeMarks)
                result.HomeMarks.Add(new KeyValuePair<Container, string>(containers[hm.ChestId], hm.BucketKey));
        }

        /// <summary>Resolve every item norm in the census to exactly ONE bucket (v2 plan §5).</summary>
        private static Dictionary<string, string> ClassifyNorms(
            List<Container> containers, FilterSpec[] specs,
            Dictionary<string, ItemDrop.ItemData> sampleByNorm, bool includeGear)
        {
            var bucketOfNorm = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var kv in sampleByNorm)
            {
                string norm = kv.Key;
                var sample = kv.Value;

                // 1. an explicit item pin or sign token anywhere that can RECEIVE it → the item gets
                //    its own bucket, anchored at the pinning chest(s). Wildcard tokens work naturally
                //    because we ask per observed norm rather than trying to expand the pattern.
                if (AnyTargetPinsItem(specs, norm))
                {
                    bucketOfNorm[norm] = BucketKeys.ForType(norm);
                    continue;
                }

                // 2. an [ItemGroups] category, resolved through the authoritative group order so an
                //    item in two groups (FlametalOre is in both `ores` and `metals` in the shipped
                //    defaults) lands somewhere fixed rather than in hash order.
                var group = Groups.FirstGroupFor(norm);
                if (group != null)
                {
                    bucketOfNorm[norm] = group;
                    continue;
                }

                bool stackable = sample.m_shared.m_maxStackSize > 1;

                // 3. non-stackable gear → weapons / armor / tools / gear:misc
                if (!stackable)
                {
                    bucketOfNorm[norm] = includeGear ? Gear.BucketFor(sample, norm) : null;
                    continue;
                }

                // 4. ungrouped stackable → its own bucket provisionally; the allocator folds it into
                //    `misc` unless it is big enough to earn a chest (§16.4.1).
                bucketOfNorm[norm] = BucketKeys.ForType(norm);
            }

            WarnAboutDeadTokens(specs, sampleByNorm);
            return bucketOfNorm;
        }

        private static bool AnyTargetPinsItem(FilterSpec[] specs, string norm)
        {
            for (int i = 0; i < specs.Length; i++)
            {
                var s = specs[i];
                if (s == null || s.Ignore || s.ManualOnly) continue;   // cannot receive → not an anchor
                if (s.MatchesItem(norm)) return true;
            }
            return false;
        }

        /// <summary>§16.4.6: a mistyped sign (`sort: metal` — not a group, so it becomes an item token
        /// that nothing matches) makes a chest an anchor for a bucket with zero demand. The allocator
        /// already refuses to reserve capacity for empty buckets, but the player gets no feedback that
        /// their label does nothing. Say so, once per token per session.</summary>
        private static readonly HashSet<string> WarnedTokens = new HashSet<string>();

        private static void WarnAboutDeadTokens(FilterSpec[] specs,
            Dictionary<string, ItemDrop.ItemData> sampleByNorm)
        {
            for (int i = 0; i < specs.Length; i++)
            {
                var s = specs[i];
                if (s == null) continue;
                foreach (var token in s.Items)
                {
                    if (Groups.IsGroup(token)) continue;
                    bool hit = false;
                    foreach (var norm in sampleByNorm.Keys)
                        if (Names.Matches(token, norm)) { hit = true; break; }
                    if (hit) continue;
                    if (!WarnedTokens.Add(token)) continue;
                    Plugin.Log.LogWarning("[organize] filter token '" + token +
                        "' matches no item in range and is not a group name - that label does nothing. " +
                        "Valid groups: " + string.Join(", ", Groups.GroupsInOrder()));
                }
            }
        }

        /// <summary>Bucket key → reason this chest is its home.</summary>
        private static Dictionary<string, AnchorKind> BuildAnchors(
            FilterSpec spec, List<string> stations,
            Dictionary<string, string> bucketOfNorm, bool excludedAsTarget)
        {
            // A chest that can never receive is never an anchor — reserving capacity in it would take
            // slots out of the free pool for nothing.
            if (excludedAsTarget) return null;

            var anchors = new Dictionary<string, AnchorKind>(StringComparer.Ordinal);

            // explicit item pins/sign tokens → the per-type buckets they name
            if (spec.Items.Count > 0)
            {
                foreach (var kv in bucketOfNorm)
                {
                    if (!BucketKeys.IsPerType(kv.Value)) continue;
                    if (!spec.MatchesItem(kv.Key)) continue;
                    Promote(anchors, kv.Value, AnchorKind.Pin);
                }
            }

            // sign group tokens → the group buckets
            foreach (var g in spec.GroupNames)
                Promote(anchors, g, AnchorKind.Sign);

            // an adjacent crafting station attracts its mapped groups
            if (stations != null)
                for (int i = 0; i < stations.Count; i++)
                    Promote(anchors, stations[i], AnchorKind.Station);

            // a home claimed by a previous run — the allocator's fixed point (§4.1)
            if (!string.IsNullOrEmpty(spec.Home))
                Promote(anchors, spec.Home, AnchorKind.Home);

            return anchors.Count > 0 ? anchors : null;
        }

        private static void Promote(Dictionary<string, AnchorKind> anchors, string bucket, AnchorKind kind)
        {
            if (string.IsNullOrEmpty(bucket)) return;
            if (!anchors.TryGetValue(bucket, out var existing) || kind > existing)
                anchors[bucket] = kind;
        }

        /// <summary>One spatial station pass for the whole run, then a per-chest lookup — replaces the
        /// per-chest <c>Stations.GroupsForChest</c> scan (§15.2 fix 1).</summary>
        private static List<string>[] BuildStationIndex(List<Container> containers, float range)
        {
            int n = containers.Count;
            var result = new List<string>[n];
            if (n == 0) return result;

            // One query centred on the origin, wide enough to cover every candidate plus the station
            // match distance, so distant stations near far chests are still seen.
            var origin = containers[0].transform.position;
            float widest = 0f;
            for (int i = 0; i < n; i++)
            {
                float d = Vector3.Distance(origin, containers[i].transform.position);
                if (d > widest) widest = d;
            }

            // Fresh pass for a plan: a plan is a deliberate user action, so pay for accuracy here
            // rather than reusing a cached tick-path result.
            var hits = Stations.HitsAround(origin, widest + range, 0f);

            for (int i = 0; i < n; i++)
                result[i] = Stations.GroupsNear(hits, containers[i].transform.position, range);

            return result;
        }

        /// <summary>Total order over bucket keys for the allocator's demand tie-break: groups in their
        /// authoritative order, then gear, then misc, then per-type buckets.</summary>
        private static Func<string, int> BuildBucketRanker()
        {
            var order = Groups.GroupsInOrder();
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < order.Count; i++) rank[order[i]] = i;
            int groupCount = order.Count;

            rank[BucketKeys.Weapons] = groupCount + 0;
            rank[BucketKeys.Armor] = groupCount + 1;
            rank[BucketKeys.Tools] = groupCount + 2;
            rank[BucketKeys.GearMisc] = groupCount + 3;
            rank[BucketKeys.Misc] = groupCount + 4;
            int perType = groupCount + 5;

            return key => key != null && rank.TryGetValue(key, out var r) ? r : perType;
        }

        // ---- execute ------------------------------------------------------------------------------

        /// <summary>True while a plan is executing. A run spans many frames, and a second plan built
        /// during it cannot see the first one's in-flight moves: our to = (-1,-1) remove creates no
        /// InventoryBlock, and the destination add only lands on the RPC response. Two overlapping runs
        /// would both see the same stack as present and both issue a remove for it. (1.1.2)</summary>
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
                Msg("Organize already running");
                return;
            }

            // The claim is recorded now, at confirm time, rather than at plan time — a previewed plan
            // that is never confirmed must not leave homes marked. v2 plan §4.1.
            ApplyHomeMarks(plan);

            Plugin.Log.LogInfo("[organize] executing " + plan.Moves.Count + " move(s)");
            _running = true;
            Plugin.Instance.StartCoroutine(Run(plan));
        }

        private static void ApplyHomeMarks(OrganizePlan plan)
        {
            int set = 0, cleared = 0;
            foreach (var hm in plan.HomeMarks)
            {
                if (hm.Key == null) continue;
                Filters.SetHome(hm.Key, hm.Value);
                if (hm.Value == null) cleared++; else set++;
            }
            if (set > 0 || cleared > 0)
                Plugin.Log.LogInfo("[organize] home markers: " + set + " claimed, " + cleared + " released");
        }

        private sealed class Pending
        {
            public UnityMove Move;
            public int Attempts;
        }

        private enum Outcome { Issued, Retry, Drop }

        /// <summary>An issued MUC request we are still waiting on.</summary>
        private struct Outstanding
        {
            public int RequestId;
            public float Deadline;
        }

        private static IEnumerator Run(OrganizePlan plan)
        {
            try
            {
                int perSecond = Throttle.MovesPerSecond(OrganizeConfig.MovesPerSecondValue);
                int maxThisRun = OrganizeConfig.MaxMovesPerRunValue;

                int issued = 0, movedItems = 0;
                int skippedMoves = 0, skippedItems = 0, droppedRequests = 0;
                var targetsHit = new HashSet<Container>();

                // §16.2.3: Router.Room reads a LOCAL inventory that does not reflect in-flight adds,
                // so N moves into one chest all saw the same free space and all issued. Debit what we
                // have already promised each destination for the whole run.
                var promised = new Dictionary<Container, int>();
                var outstanding = new List<Outstanding>();

                var queue = new List<Pending>(plan.Moves.Count);
                foreach (var mv in plan.Moves) queue.Add(new Pending { Move = mv });

                float tokens = perSecond;
                float lastRefill = Time.realtimeSinceStartup;
                bool capped = false;

                while (queue.Count > 0 && !capped)
                {
                    var retry = new List<Pending>();
                    int issuedThisDrain = 0;

                    foreach (var pm in queue)
                    {
                        if (issued >= maxThisRun) { capped = true; retry.Add(pm); continue; }

                        // ---- rate: a real per-SECOND budget (§16.3) --------------------------------
                        while (tokens < 1f)
                        {
                            yield return null;
                            float now = Time.realtimeSinceStartup;
                            tokens += (now - lastRefill) * perSecond;
                            lastRefill = now;
                            if (tokens > perSecond) tokens = perSecond;
                        }

                        // ---- backpressure: our own ledger, not InventoryBlock (§15.6) --------------
                        while (CountOutstanding(outstanding, ref droppedRequests) >= MaxOutstanding)
                            yield return null;

                        var timer = Throttle.Measure();
                        Outcome outcome;
                        int amount;
                        int requestId;
                        try
                        {
                            outcome = TryIssue(pm.Move, promised, out amount, out requestId);
                        }
                        finally
                        {
                            timer.Dispose();
                        }

                        if (outcome == Outcome.Issued)
                        {
                            tokens -= 1f;
                            issued++;
                            issuedThisDrain++;
                            movedItems += amount;
                            targetsHit.Add(pm.Move.Target);
                            if (requestId != 0)
                                outstanding.Add(new Outstanding
                                {
                                    RequestId = requestId,
                                    Deadline = Time.realtimeSinceStartup + OutstandingDeadline,
                                });
                        }
                        else if (outcome == Outcome.Retry && ++pm.Attempts < MaxAttemptsPerMove)
                        {
                            retry.Add(pm);
                        }
                        else
                        {
                            skippedMoves++;
                            skippedItems += pm.Move.Amount;
                        }
                    }

                    // §16.2.5 termination: a full drain that issued nothing will never issue anything.
                    if (issuedThisDrain == 0)
                    {
                        foreach (var pm in retry) { skippedMoves++; skippedItems += pm.Move.Amount; }
                        break;
                    }
                    queue = retry;
                }

                // Let the tail of the queue settle so the "still outstanding" number is meaningful.
                float waitUntil = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < waitUntil &&
                       CountOutstanding(outstanding, ref droppedRequests) > 0)
                    yield return null;

                Report(movedItems, targetsHit.Count, skippedItems, plan.Summary.HomelessItems,
                       droppedRequests, capped);
                Plugin.Log.LogInfo("[organize] issued " + issued + " move(s) covering " + movedItems +
                    " item(s) into " + targetsHit.Count + " chest(s); skipped " + skippedMoves +
                    " move(s) covering " + skippedItems + " item(s); " + droppedRequests +
                    " request(s) timed out; throttle scale " + Throttle.Scale.ToString("0.00"));
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>Live-revalidate a single move and, if it still holds, issue it. Nothing here trusts
        /// the plan: the plan is advisory and the world may have changed since it was built.</summary>
        private static Outcome TryIssue(UnityMove mv, Dictionary<Container, int> promised,
            out int amount, out int requestId)
        {
            amount = 0;
            requestId = 0;

            var src = mv.Source;
            var tgt = mv.Target;
            var item = mv.Item;
            if (src == null || tgt == null || item == null || item.m_shared == null) return Outcome.Drop;

            var sInv = src.GetInventory();
            var tInv = tgt.GetInventory();
            if (sInv == null || tInv == null) return Outcome.Drop;

            // stack gone since the plan was built
            if (!sInv.GetAllItems().Contains(item)) return Outcome.Drop;

            // §16.2.4: re-resolve BOTH endpoints. A chest destroyed or unloaded mid-run spills its
            // contents as ground drops on its owner's client, and issuing a remove against that dying
            // ZDO is exactly the window where an item can exist twice.
            var srcNv = SorterZdo.NView(src);
            if (srcNv == null || !srcNv.IsValid()) return Outcome.Drop;
            var tgtNv = SorterZdo.NView(tgt);
            if (tgtNv == null || !tgtNv.IsValid()) return Outcome.Drop;

            var sBlock = InventoryBlock.Get(sInv);
            if (sBlock != null && sBlock.IsSlotBlocked(item.m_gridPos)) return Outcome.Retry;

            // §16.2.7: wards and per-container access were plan-time only, so a ward raised mid-run was
            // bypassed via ClaimOwnership + MUC. Re-check both endpoints every time.
            if (!SorterZdo.PlayerCanAccess(tgt) || !SorterZdo.PlayerCanAccess(src)) return Outcome.Drop;
            if (!PrivateArea.CheckAccess(tgt.transform.position, 0f, false, true)) return Outcome.Drop;
            if (!PrivateArea.CheckAccess(src.transform.position, 0f, false, true)) return Outcome.Drop;

            // Router.Room answers "can one more fit?" for a non-stackable — it returns 1 whenever the
            // chest has any empty slot at all. That is right for the sorter tick, which moves a single
            // stack per tick, but wrong here: `promised` counts ITEMS issued this run, so the first
            // gear piece would promise 1, and every later gear move into the same chest would compute
            // 1 - 1 = 0 and defer itself to the next press. In-game that showed up as Organize needing
            // several presses, each one placing exactly one more tool/weapon. Count the slots instead.
            int room = item.m_shared.m_maxStackSize <= 1
                ? tInv.GetEmptySlots()
                : Router.Room(tInv, item);
            if (promised.TryGetValue(tgt, out var already)) room -= already;
            if (room <= 0) return Outcome.Retry;            // may drain later in the run

            amount = Math.Min(mv.Amount, Math.Min(item.m_stack, room));
            if (amount <= 0) return Outcome.Retry;

            // §16.2.2: Container.m_inUse is a LOCAL field, so a remote player browsing this chest is
            // invisible to us and the claim below would strand their deposit (vanilla Container.Save
            // is owner-gated). Gate on ZDO ownership instead — that IS networked — and keep the local
            // in-use check as a second signal for chests open on this client.
            if (tgt.IsInUse()) return Outcome.Retry;
            long owner = tgtNv.GetZDO().GetOwner();
            if (owner != 0L && !tgtNv.IsOwner()) return Outcome.Retry;   // someone else holds it

            if (!tgtNv.IsOwner()) tgtNv.ClaimOwnership();

            // exactly Puller's transfer primitive — the ONLY sanctioned write path
            var request = ContainerHandler.RemoveItemFromChest(
                src, item, tInv, new Vector2i(-1, -1),
                tgtNv.GetZDO().m_uid, amount, null);

            requestId = request != null ? request.RequestID : 0;
            promised[tgt] = already + amount;
            return Outcome.Issued;
        }

        /// <summary>How many of our issued requests MUC has not yet answered. A request disappears
        /// from PackageHandler when its response is processed, so this is a real completion signal —
        /// and one that needs no patch into MUC. Requests past their deadline are released with a log
        /// line, because MUC has no timeout or sweep of its own (§15.6).</summary>
        private static int CountOutstanding(List<Outstanding> outstanding, ref int dropped)
        {
            float now = Time.realtimeSinceStartup;
            int live = 0;
            for (int i = outstanding.Count - 1; i >= 0; i--)
            {
                var o = outstanding[i];
                bool stillQueued = PackageHandler.GetPackage<RequestChestRemove>(o.RequestId, out var pkg) && pkg != null;
                if (!stillQueued) { outstanding.RemoveAt(i); continue; }

                if (now >= o.Deadline)
                {
                    outstanding.RemoveAt(i);
                    dropped++;
                    Plugin.Log.LogWarning("[organize] MUC request " + o.RequestId +
                        " got no response within " + OutstandingDeadline.ToString("0") +
                        " s; releasing our reservation for it");
                    continue;
                }
                live++;
            }
            return live;
        }

        private static void Report(int movedItems, int chests, int skippedItems, int homeless,
                                   int droppedRequests, bool capped)
        {
            string msg = "Organized " + movedItems + " item" + (movedItems == 1 ? "" : "s") +
                         " into " + chests + " chest" + (chests == 1 ? "" : "s");
            if (skippedItems > 0) msg += " (" + skippedItems + " could not move)";
            if (homeless > 0) msg += "; " + homeless + " had no room - add more chests";
            if (droppedRequests > 0) msg += "; " + droppedRequests + " transfer(s) timed out";
            if (capped) msg += ". Press Organize again to continue";
            Msg(msg);
        }

        private static void Msg(string text)
        {
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }
    }
}
