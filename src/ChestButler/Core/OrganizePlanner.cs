using System;
using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>One movable stack seen inside a chest. <see cref="BucketKey"/> is resolved by the
    /// Unity adapter (pins/sign → [ItemGroups] → gear by m_itemType → misc) and is null for a stack
    /// the adapter has decided must stay where it is.</summary>
    internal struct StackView
    {
        public string Norm;
        public int Count;
        public bool Stackable;      // m_maxStackSize > 1
        public string BucketKey;    // null = immovable, stays put and keeps occupying its slot
    }

    /// <summary>Why a chest is a bucket's home, strongest last. This order is the FILL order when a
    /// bucket has several homes, so an explicitly pinned chest fills before a chest that merely sits
    /// near a forge, which fills before a chest the allocator adopted on its own.
    ///
    /// <see cref="Home"/> — the <c>psort_home</c> marker from a previous run (v2 plan §4.1) — is
    /// deliberately the WEAKEST anchor, and getting that wrong breaks convergence. A freshly claimed
    /// chest has no anchor kind at all on run 1, and carries Home on run 2. If Home outranked Station,
    /// a bucket with both a station chest and a claimed chest would fill station-first on run 1 and
    /// claimed-first on run 2, so the items would swap back and forth on every press and the
    /// acceptance test in §12 could never pass. Ranking Home lowest keeps the fill order identical
    /// across runs, because a claimed chest only ever moves from "no anchor" to "the weakest anchor",
    /// never past a peer.</summary>
    internal enum AnchorKind
    {
        None = 0,
        Home = 1,
        Station = 2,
        Sign = 3,
        Pin = 4,
    }

    /// <summary>A chest as the allocator sees it. Deliberately Unity-free — capacity and affinity
    /// arrive as plain data or delegates — so the whole allocator is unit-testable with no game.
    ///
    /// CONTRACT: the adapter supplies chests ordered by (distance, ZDO uid), which is exactly what
    /// <c>ContainerTracker.CandidatesWithDistance</c> guarantees since 1.1.2. The allocator still
    /// breaks every tie on <see cref="UidKey"/> explicitly rather than trusting input order, so it
    /// is deterministic on its own terms (v2 plan §16.4.7).</summary>
    internal sealed class ChestView
    {
        public int Id;
        public string UidKey = "";                 // stable cross-session identity; final tie-break
        public float Distance;                     // metres from the origin sorter
        public int TotalSlots;                     // GetWidth() * GetHeight() — supports modded sizes
        public int Priority;                       // sign priority (pN)
        public List<StackView> Stacks;

        /// <summary>Never a destination: Sorter chest, <c>sort: off</c>, or the Manual toggle.</summary>
        public bool ExcludedAsTarget;

        /// <summary>Never a source either — <c>sort: off</c> / <c>ignore</c> only. v1 collapsed this
        /// into ExcludedAsTarget and never tested it during stack enrollment, so a personal stash
        /// marked <c>sort: off</c> was emptied into the communal base on the first press. v2 finds a
        /// home for *everything*, which escalates that from "sometimes leaks" to "always loots"
        /// (v2 plan §4 exclusion table, §16.4.5).</summary>
        public bool ExcludedAsSource;

        /// <summary>Bucket key → strongest reason this chest is that bucket's home.</summary>
        public Dictionary<string, AnchorKind> Anchors;

        /// <summary>The <c>psort_home</c> value currently written on this chest, or null. Read so the
        /// allocator can clear markers it no longer needs (v2 plan §4.1) — a base that shrinks must
        /// not keep dead homes reserved forever.</summary>
        public string HomeMarker;

        public AnchorKind AnchorFor(string bucket)
        {
            if (Anchors == null || bucket == null) return AnchorKind.None;
            return Anchors.TryGetValue(bucket, out var k) ? k : AnchorKind.None;
        }

        public bool IsAnchor => Anchors != null && Anchors.Count > 0;

        public int HeldOf(string norm)
        {
            int n = 0;
            if (Stacks != null)
                for (int i = 0; i < Stacks.Count; i++)
                    if (Stacks[i].Norm == norm) n += Stacks[i].Count;
            return n;
        }
    }

    /// <summary>Move <c>Amount</c> of <c>Norm</c> out of stack <c>SrcStackIndex</c> in chest
    /// <c>SrcId</c> and into chest <c>TgtId</c>.</summary>
    internal struct OrganizeMove
    {
        public int SrcId;
        public int SrcStackIndex;
        public int TgtId;
        public string Norm;
        public int Amount;
    }

    /// <summary>Write (<c>BucketKey</c> set) or clear (<c>BucketKey</c> null) a chest's
    /// <c>psort_home</c> marker. v2 plan §4.1.</summary>
    internal struct HomeMark
    {
        public int ChestId;
        public string BucketKey;
    }

    internal struct OrganizeSummary
    {
        public int TotalItems;      // sum of moved amounts
        public int TargetChests;    // distinct destinations
        public int SourceChests;    // distinct sources
        public int HomelessItems;   // planned-but-unplaceable: "N items had no room"
        public int BucketsPlanned;
    }

    /// <summary>Everything the allocator needs that is not a chest. Grouped into one object so the
    /// signature stays readable and the offline tests can build a minimal input.</summary>
    internal sealed class PlannerInput
    {
        public IReadOnlyList<ChestView> Chests;

        /// <summary>Max stack size for an item norm (1 = non-stackable). A property of the item type,
        /// not of a chest, which is why it is here rather than on ChestView.</summary>
        public Func<string, int> MaxStackOf;

        /// <summary>Total order over bucket keys, used only to break demand ties. §16.4.1: bucket keys
        /// are strings, so §4 step 3's "bucket enum order" tie-break does not apply to them — the
        /// adapter supplies an explicit rank (group order, then gear, then misc, then per-type).</summary>
        public Func<string, int> BucketRank;

        /// <summary>Distance between two chests by id. §4 step 4b prefers an empty chest near the
        /// bucket's existing home; ChestView only carries distance-to-origin, so the adapter closes
        /// over the real positions.</summary>
        public Func<int, int, float> DistanceBetween;

        /// <summary>An ungrouped stackable only earns its own bucket when its slot demand exceeds
        /// this; otherwise it folds into <c>misc</c> (§16.4.1). Default is one vanilla chest.</summary>
        public int MiscPromoteSlots = 24;
    }

    internal sealed class PlannerResult
    {
        public readonly List<OrganizeMove> Moves = new List<OrganizeMove>();
        public readonly List<HomeMark> HomeMarks = new List<HomeMark>();
        public OrganizeSummary Summary;
    }

    /// <summary>PURE, deterministic whole-base allocator (Organize v2).
    ///
    /// census → classify → per-bucket slot demand → allocate SLOTS of chests to buckets → decide the
    /// final distribution → diff against what is already there → moves.
    ///
    /// The diff step is what makes churn minimisation structural rather than a special case: the plan
    /// is the difference between the current layout and the intended one, so anything already in the
    /// right chest generates no move at all. Together with the persisted <c>psort_home</c> marker
    /// (v2 plan §4.1) that is what lets a second Organize on a tidy base move exactly zero items —
    /// the acceptance test in §12.
    ///
    /// No Unity, no config, no randomness, no clock.</summary>
    internal static class OrganizePlanner
    {
        private struct Holder
        {
            public int ChestId;
            public int StackIndex;
            public int Count;
        }

        /// <summary>Slots of one chest reserved for one bucket.</summary>
        private sealed class Reservation
        {
            public int ChestId;
            public AnchorKind Kind;
            public int Priority;
            public float Distance;
            public string UidKey;
            public int Slots;
            public bool Claimed;   // the allocator took this chest itself → gets a psort_home marker
        }

        internal static PlannerResult Plan(PlannerInput input)
        {
            var result = new PlannerResult();
            if (input?.Chests == null || input.Chests.Count == 0) return result;

            var chests = input.Chests;
            Func<string, int> maxStackOf = input.MaxStackOf ?? (_ => 1);
            Func<string, int> bucketRank = input.BucketRank ?? (_ => 0);
            Func<int, int, float> distanceBetween = input.DistanceBetween ?? ((_, __) => 0f);
            int promoteSlots = Math.Max(1, input.MiscPromoteSlots);

            // ---- 1. enrol every movable stack, bucketed ------------------------------------------
            // holdersByBucket[bucket][norm] = who currently holds it and where.
            var holdersByBucket = new Dictionary<string, Dictionary<string, List<Holder>>>(StringComparer.Ordinal);
            var bucketOrderSeen = new List<string>();
            var immovableSlots = new int[chests.Count];

            for (int ci = 0; ci < chests.Count; ci++)
            {
                var c = chests[ci];
                if (c?.Stacks == null) continue;

                for (int si = 0; si < c.Stacks.Count; si++)
                {
                    var s = c.Stacks[si];
                    if (string.IsNullOrEmpty(s.Norm) || s.Count <= 0) continue;

                    // sort: off — never read, never written. Its slots are not ours to plan with.
                    if (c.ExcludedAsSource || string.IsNullOrEmpty(s.BucketKey))
                    {
                        immovableSlots[ci]++;
                        continue;
                    }

                    if (!holdersByBucket.TryGetValue(s.BucketKey, out var byNorm))
                    {
                        byNorm = new Dictionary<string, List<Holder>>(StringComparer.Ordinal);
                        holdersByBucket[s.BucketKey] = byNorm;
                        bucketOrderSeen.Add(s.BucketKey);
                    }
                    if (!byNorm.TryGetValue(s.Norm, out var list))
                    {
                        list = new List<Holder>();
                        byNorm[s.Norm] = list;
                    }
                    list.Add(new Holder { ChestId = ci, StackIndex = si, Count = s.Count });
                }
            }

            // ---- 2. fold undersized per-type buckets into misc (§16.4.1) --------------------------
            FoldSmallTypeBuckets(chests, holdersByBucket, bucketOrderSeen, maxStackOf, promoteSlots);

            // ---- 3. slot demand per bucket -------------------------------------------------------
            var demand = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in holdersByBucket)
            {
                int slots = 0;
                foreach (var norm in kv.Value)
                {
                    int total = 0;
                    for (int i = 0; i < norm.Value.Count; i++) total += norm.Value[i].Count;
                    slots += CeilDiv(total, Math.Max(1, maxStackOf(norm.Key)));
                }
                demand[kv.Key] = slots;
            }

            // ---- 4. capacity ledger --------------------------------------------------------------
            // A free chest offers its FULL size even when it currently holds other buckets' items:
            // those items are themselves being reassigned, so the chest drains during execution
            // (v2 plan §4 step 4). Only genuinely immovable stacks permanently cost slots.
            var slotsLeft = new int[chests.Count];
            for (int i = 0; i < chests.Count; i++)
            {
                var c = chests[i];
                slotsLeft[i] = c == null || c.ExcludedAsTarget
                    ? 0
                    : Math.Max(0, c.TotalSlots - immovableSlots[i]);
            }

            // ---- 5. allocate slots to buckets ----------------------------------------------------
            var buckets = new List<string>(demand.Keys);
            buckets.Sort((a, b) =>
            {
                int d = demand[b].CompareTo(demand[a]);          // largest demand first
                if (d != 0) return d;
                d = bucketRank(a).CompareTo(bucketRank(b));
                return d != 0 ? d : string.CompareOrdinal(a, b);
            });

            // Only a bucket that actually has items in the census can reserve a chest. §16.4.6: a
            // mistyped sign, a renamed group or a stale pin otherwise makes a chest an anchor for a
            // bucket with zero demand, and that chest is then never repurposed — silently removed from
            // the free pool forever while the player wonders why nothing uses it.
            var liveBuckets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in buckets) if (demand[b] > 0) liveBuckets.Add(b);

            var homes = new Dictionary<string, List<Reservation>>(StringComparer.Ordinal);
            foreach (var bucket in buckets)
                homes[bucket] = Allocate(bucket, demand[bucket], chests, slotsLeft,
                                         distanceBetween, liveBuckets);

            // ---- 6. final distribution + diff ----------------------------------------------------
            var moves = result.Moves;
            int homeless = 0;
            foreach (var bucket in buckets)
                homeless += DistributeAndDiff(bucket, holdersByBucket[bucket], homes[bucket],
                                              chests, slotsLeft, maxStackOf, moves);

            // ---- 7. evict before fill ------------------------------------------------------------
            OrderEvictionsFirst(moves);

            // ---- 8. psort_home bookkeeping -------------------------------------------------------
            EmitHomeMarks(chests, homes, result.HomeMarks);

            // ---- 9. summary ----------------------------------------------------------------------
            var srcSet = new HashSet<int>();
            var tgtSet = new HashSet<int>();
            int moved = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                moved += moves[i].Amount;
                srcSet.Add(moves[i].SrcId);
                tgtSet.Add(moves[i].TgtId);
            }
            result.Summary = new OrganizeSummary
            {
                TotalItems = moved,
                SourceChests = srcSet.Count,
                TargetChests = tgtSet.Count,
                HomelessItems = homeless,
                BucketsPlanned = buckets.Count,
            };
            return result;
        }

        // ------------------------------------------------------------------------------------------

        /// <summary>§16.4.1: 40+ vanilla items match none of the 13 default groups, and giving each its
        /// own bucket meant 3 Queen Bees + 1 Wisp + 12 Resin claimed three 24-slot chests for 16
        /// items. A per-type bucket has to earn its chest; otherwise its stacks join <c>misc</c>.
        ///
        /// An *anchored* per-type bucket is exempt — a pin or a sign naming that item is a direct
        /// instruction, and quantity does not get to overrule it.</summary>
        private static void FoldSmallTypeBuckets(
            IReadOnlyList<ChestView> chests,
            Dictionary<string, Dictionary<string, List<Holder>>> holdersByBucket,
            List<string> bucketOrderSeen,
            Func<string, int> maxStackOf,
            int promoteSlots)
        {
            var fold = new List<string>();
            foreach (var key in bucketOrderSeen)
            {
                if (!BucketKeys.IsPerType(key)) continue;
                if (!holdersByBucket.TryGetValue(key, out var byNorm)) continue;
                if (AnyChestAnchors(chests, key)) continue;

                int slots = 0;
                foreach (var norm in byNorm)
                {
                    int total = 0;
                    for (int i = 0; i < norm.Value.Count; i++) total += norm.Value[i].Count;
                    slots += CeilDiv(total, Math.Max(1, maxStackOf(norm.Key)));
                }
                if (slots <= promoteSlots) fold.Add(key);
            }

            if (fold.Count == 0) return;

            if (!holdersByBucket.TryGetValue(BucketKeys.Misc, out var misc))
            {
                misc = new Dictionary<string, List<Holder>>(StringComparer.Ordinal);
                holdersByBucket[BucketKeys.Misc] = misc;
                bucketOrderSeen.Add(BucketKeys.Misc);
            }

            foreach (var key in fold)
            {
                foreach (var norm in holdersByBucket[key])
                {
                    if (!misc.TryGetValue(norm.Key, out var list))
                    {
                        list = new List<Holder>();
                        misc[norm.Key] = list;
                    }
                    list.AddRange(norm.Value);
                }
                holdersByBucket.Remove(key);
                bucketOrderSeen.Remove(key);
            }
        }

        private static bool AnyChestAnchors(IReadOnlyList<ChestView> chests, string bucket)
        {
            for (int i = 0; i < chests.Count; i++)
                if (chests[i] != null && chests[i].AnchorFor(bucket) != AnchorKind.None) return true;
            return false;
        }

        /// <summary>Reserve slots for one bucket: its anchors first, then claimed free chests.
        ///
        /// §16.4.2 is the correction that shapes this: v1's "subtract anchor capacity per bucket"
        /// double-counted a shared chest — <c>$piece_cauldron = "cooking, meat, seeds"</c> seeded one
        /// 24-slot kitchen chest into three buckets and subtracted 24 slots from each, so nothing
        /// claimed a free chest and thousands of items were reported homeless while empty chests sat
        /// two rooms away. Slots are debited from ONE ledger, exactly once.</summary>
        private static List<Reservation> Allocate(
            string bucket, int needed,
            IReadOnlyList<ChestView> chests,
            int[] slotsLeft,
            Func<int, int, float> distanceBetween,
            HashSet<string> liveBuckets)
        {
            var taken = new List<Reservation>();
            if (needed <= 0) return taken;

            // -- anchors ---------------------------------------------------------------------------
            var anchors = new List<Reservation>();
            for (int i = 0; i < chests.Count; i++)
            {
                var c = chests[i];
                if (c == null || c.ExcludedAsTarget) continue;
                var kind = c.AnchorFor(bucket);
                if (kind == AnchorKind.None) continue;
                anchors.Add(new Reservation
                {
                    ChestId = i, Kind = kind, Priority = c.Priority,
                    Distance = c.Distance, UidKey = c.UidKey ?? "",
                });
            }
            anchors.Sort(CompareAnchors);

            foreach (var a in anchors)
            {
                if (needed <= 0) break;
                int take = Math.Min(slotsLeft[a.ChestId], needed);
                if (take <= 0) continue;
                slotsLeft[a.ChestId] -= take;
                needed -= take;
                a.Slots = take;
                taken.Add(a);
            }

            // -- claim free chests -----------------------------------------------------------------
            // Reference point for "nearest": the bucket's own primary home if it has one, so a
            // spilling category grows outward from its anchor rather than from the clicked sorter.
            while (needed > 0)
            {
                int refChest = taken.Count > 0 ? taken[0].ChestId : -1;
                int pick = PickFreeChest(bucket, chests, slotsLeft, refChest, distanceBetween, liveBuckets);
                if (pick < 0) break;                                  // free pool exhausted

                int take = Math.Min(slotsLeft[pick], needed);
                slotsLeft[pick] -= take;
                needed -= take;
                var c = chests[pick];
                taken.Add(new Reservation
                {
                    ChestId = pick, Kind = AnchorKind.None, Priority = c.Priority,
                    Distance = c.Distance, UidKey = c.UidKey ?? "", Slots = take,
                    Claimed = true,        // → gets a psort_home marker so run 2 finds it again
                });
            }

            return taken;
        }

        private static int CompareAnchors(Reservation a, Reservation b)
        {
            int d = ((int)b.Kind).CompareTo((int)a.Kind);            // strongest reason first
            if (d != 0) return d;
            d = b.Priority.CompareTo(a.Priority);                    // sign pN
            if (d != 0) return d;
            d = a.Distance.CompareTo(b.Distance);
            return d != 0 ? d : string.CompareOrdinal(a.UidKey, b.UidKey);
        }

        /// <summary>§4 step 4's preference list: an empty chest wins over a chest that still holds
        /// something, then nearest to the bucket's home (or to the origin sorter when it has none),
        /// then ZDO uid so a symmetric storage hall does not flip between sessions.</summary>
        private static int PickFreeChest(
            string bucket,
            IReadOnlyList<ChestView> chests,
            int[] slotsLeft,
            int refChest,
            Func<int, int, float> distanceBetween,
            HashSet<string> liveBuckets)
        {
            int best = -1;
            bool bestEmpty = false;
            float bestDist = 0f;
            string bestUid = null;

            for (int i = 0; i < chests.Count; i++)
            {
                if (slotsLeft[i] <= 0) continue;
                var c = chests[i];
                if (c == null || c.ExcludedAsTarget) continue;

                // Never steal a chest that is another LIVE bucket's home — that bucket would then
                // relocate on the next press and neither would ever settle. An anchor for a bucket
                // with no items in the census is dead and its chest stays claimable (§16.4.6).
                if (AnchorsAnotherLiveBucket(c, bucket, liveBuckets)) continue;

                bool empty = slotsLeft[i] >= c.TotalSlots;
                float dist = refChest >= 0 ? distanceBetween(refChest, i) : c.Distance;
                string uid = c.UidKey ?? "";

                if (best < 0 || Better(empty, dist, uid, bestEmpty, bestDist, bestUid))
                {
                    best = i; bestEmpty = empty; bestDist = dist; bestUid = uid;
                }
            }
            return best;
        }

        private static bool Better(bool empty, float dist, string uid,
                                   bool bestEmpty, float bestDist, string bestUid)
        {
            if (empty != bestEmpty) return empty;
            int d = dist.CompareTo(bestDist);
            if (d != 0) return d < 0;
            return string.CompareOrdinal(uid, bestUid) < 0;
        }

        private static bool AnchorsAnotherLiveBucket(ChestView c, string bucket, HashSet<string> liveBuckets)
        {
            if (c.Anchors == null) return false;
            foreach (var kv in c.Anchors)
            {
                if (string.Equals(kv.Key, bucket, StringComparison.Ordinal)) continue;
                if (kv.Value < AnchorKind.Home) continue;
                if (liveBuckets != null && !liveBuckets.Contains(kv.Key)) continue;   // dead anchor
                return true;
            }
            return false;
        }

        /// <summary>Decide where every stack of a bucket ends up, then emit only the differences.
        /// Returns the number of items that found no room.</summary>
        private static int DistributeAndDiff(
            string bucket,
            Dictionary<string, List<Holder>> byNorm,
            List<Reservation> homes,
            IReadOnlyList<ChestView> chests,
            int[] slotsLeft,
            Func<string, int> maxStackOf,
            List<OrganizeMove> moves)
        {
            int homeless = 0;
            if (byNorm == null || byNorm.Count == 0) return 0;

            // Deterministic norm order. Bigger piles are placed first so they get the contiguous
            // capacity; equal piles fall back to the name.
            var norms = new List<string>(byNorm.Keys);
            norms.Sort((a, b) =>
            {
                int ta = 0, tb = 0;
                var la = byNorm[a]; var lb = byNorm[b];
                for (int i = 0; i < la.Count; i++) ta += la[i].Count;
                for (int i = 0; i < lb.Count; i++) tb += lb[i].Count;
                int d = tb.CompareTo(ta);
                return d != 0 ? d : string.CompareOrdinal(a, b);
            });

            // Per-home remaining slot budget, plus whatever is still unreserved in that chest so a
            // norm split across two chests does not fail on rounding alone.
            var budget = new Dictionary<int, int>();
            for (int i = 0; i < homes.Count; i++)
                budget[homes[i].ChestId] = homes[i].Slots;

            if (homes.Count == 0)
            {
                foreach (var norm in norms)
                {
                    var hs = byNorm[norm];
                    for (int i = 0; i < hs.Count; i++) homeless += hs[i].Count;
                }
                return homeless;
            }

            // ONE fixed fill order for the whole bucket, computed from anchor strength and geometry
            // only — never from what the chests currently hold.
            //
            // This is what makes re-run stability provable rather than lucky. An earlier version sorted
            // per norm by "who already holds the most of this item", which reads as churn minimisation
            // but makes the target layout a function of the CURRENT layout: a norm that got its second
            // choice on run 1 prefers where it landed on run 2, which frees its first choice for
            // another norm, which then moves. On a 400-chest base that was ~19 residual moves on the
            // second press, settling only on the third — and the acceptance test in §12 says the SECOND
            // press moves zero. With the order fixed, the computed target is a pure function of
            // (demand, geometry, anchors); run 1 makes the base match it, so run 2 diffs to nothing.
            //
            // Churn minimisation is not lost, it just lives in the diff: anything already at its
            // computed target generates no move at all.
            var fillOrder = new List<Reservation>(homes);
            fillOrder.Sort(CompareAnchors);

            foreach (var norm in norms)
            {
                var holders = byNorm[norm];
                int maxStack = Math.Max(1, maxStackOf(norm));

                int total = 0;
                for (int i = 0; i < holders.Count; i++) total += holders[i].Count;

                // ---- target distribution for this norm --------------------------------------------
                // Consolidation + churn minimisation: fill the home that already holds the most of
                // this item first, so what is already correctly placed generates no move.
                var target = new Dictionary<int, int>();
                int left = total;
                foreach (var h in fillOrder)
                {
                    if (left <= 0) break;
                    int slots = budget.TryGetValue(h.ChestId, out var b) ? b : 0;
                    int spare = slotsLeft[h.ChestId];
                    int usable = slots + spare;
                    if (usable <= 0) continue;

                    int fits = Math.Min(left, usable * maxStack);
                    if (fits <= 0) continue;

                    int slotsUsed = CeilDiv(fits, maxStack);
                    int fromBudget = Math.Min(slotsUsed, slots);
                    int fromSpare = slotsUsed - fromBudget;
                    budget[h.ChestId] = slots - fromBudget;
                    slotsLeft[h.ChestId] = spare - fromSpare;

                    target[h.ChestId] = (target.TryGetValue(h.ChestId, out var t) ? t : 0) + fits;
                    left -= fits;
                }
                homeless += left;

                // ---- diff current vs target ------------------------------------------------------
                // Surplus chests give, deficit chests take. Anything already where it belongs is
                // simply absent from both lists — that is the churn minimisation.
                var surplus = new List<Holder>();
                var current = new Dictionary<int, int>();
                for (int i = 0; i < holders.Count; i++)
                {
                    var h = holders[i];
                    current[h.ChestId] = (current.TryGetValue(h.ChestId, out var v) ? v : 0) + h.Count;
                }

                var keepLeft = new Dictionary<int, int>();
                foreach (var kv in current)
                {
                    int want = target.TryGetValue(kv.Key, out var w) ? w : 0;
                    keepLeft[kv.Key] = Math.Min(kv.Value, want);
                }

                // Walk holder stacks in input order; whatever exceeds the chest's keep quota moves.
                for (int i = 0; i < holders.Count; i++)
                {
                    var h = holders[i];
                    int keep = keepLeft.TryGetValue(h.ChestId, out var k) ? k : 0;
                    int stay = Math.Min(h.Count, keep);
                    keepLeft[h.ChestId] = keep - stay;
                    int give = h.Count - stay;
                    if (give > 0) surplus.Add(new Holder { ChestId = h.ChestId, StackIndex = h.StackIndex, Count = give });
                }

                var deficits = new List<Holder>();
                foreach (var kv in target)
                {
                    int have = current.TryGetValue(kv.Key, out var v) ? v : 0;
                    int need = kv.Value - Math.Min(have, kv.Value);
                    if (need > 0) deficits.Add(new Holder { ChestId = kv.Key, Count = need });
                }
                deficits.Sort((x, y) =>
                {
                    var cx = chests[x.ChestId]; var cy = chests[y.ChestId];
                    int d = cx.Distance.CompareTo(cy.Distance);
                    return d != 0 ? d : string.CompareOrdinal(cx.UidKey ?? "", cy.UidKey ?? "");
                });

                int si2 = 0;
                for (int di = 0; di < deficits.Count && si2 < surplus.Count; di++)
                {
                    var need = deficits[di];
                    int want = need.Count;
                    while (want > 0 && si2 < surplus.Count)
                    {
                        var give = surplus[si2];
                        if (give.Count <= 0 || give.ChestId == need.ChestId) { si2++; continue; }

                        int amount = Math.Min(want, give.Count);
                        moves.Add(new OrganizeMove
                        {
                            SrcId = give.ChestId,
                            SrcStackIndex = give.StackIndex,
                            TgtId = need.ChestId,
                            Norm = norm,
                            Amount = amount,
                        });
                        want -= amount;
                        give.Count -= amount;
                        surplus[si2] = give;
                        if (give.Count == 0) si2++;
                    }
                }
            }

            return homeless;
        }

        /// <summary>Emit moves OUT of a chest before moves INTO it, so a chest about to become a
        /// bucket's home is drained of foreign items before we try to fill it. Without this, a target
        /// full of the wrong stuff stalls every move aimed at it and the retry queue has to unwind it
        /// one pass at a time (v2 plan §6, §16.4.8).
        ///
        /// One level deep, and deliberately so: a full topological order can cycle (two chests that
        /// must each drain into the other), and execution's retry queue already handles the rest.
        /// A stable partition keeps everything else in its deterministic order.</summary>
        private static void OrderEvictionsFirst(List<OrganizeMove> moves)
        {
            if (moves.Count < 2) return;

            var targets = new HashSet<int>();
            for (int i = 0; i < moves.Count; i++) targets.Add(moves[i].TgtId);

            var evictions = new List<OrganizeMove>();
            var rest = new List<OrganizeMove>();
            for (int i = 0; i < moves.Count; i++)
            {
                if (targets.Contains(moves[i].SrcId)) evictions.Add(moves[i]);
                else rest.Add(moves[i]);
            }

            moves.Clear();
            moves.AddRange(evictions);
            moves.AddRange(rest);
        }

        /// <summary>Write <c>psort_home</c> on chests the allocator claimed itself, and clear it where
        /// it is no longer earned. v2 plan §4.1:
        ///
        /// - only self-claimed chests are marked. Pin-, sign- and station-derived anchors re-derive
        ///   every run, and marking them would freeze a station chest's role after the station is
        ///   torn down.
        /// - a marker whose bucket no longer wants the chest is cleared, or a base that shrinks keeps
        ///   dead homes reserved forever.
        /// - a marker naming a bucket that no longer exists is stale: ignored, then cleared.</summary>
        private static void EmitHomeMarks(
            IReadOnlyList<ChestView> chests,
            Dictionary<string, List<Reservation>> homes,
            List<HomeMark> marks)
        {
            // chest id → the bucket it legitimately serves this run (claimed only)
            var claimed = new Dictionary<int, string>();
            foreach (var kv in homes)
                foreach (var r in kv.Value)
                    if (r.Claimed && !claimed.ContainsKey(r.ChestId))
                        claimed[r.ChestId] = kv.Key;

            // Also treat a chest that is *still* serving the bucket its marker names as legitimate,
            // even though it was seeded as an anchor (AnchorKind.Home) rather than freshly claimed —
            // its marker is already correct and must not be rewritten or cleared.
            var keep = new HashSet<int>();
            foreach (var kv in homes)
                foreach (var r in kv.Value)
                {
                    var c = chests[r.ChestId];
                    if (c?.HomeMarker != null && string.Equals(c.HomeMarker, kv.Key, StringComparison.Ordinal))
                        keep.Add(r.ChestId);
                }

            for (int i = 0; i < chests.Count; i++)
            {
                var c = chests[i];
                if (c == null) continue;

                bool hasMarker = !string.IsNullOrEmpty(c.HomeMarker);
                claimed.TryGetValue(i, out var nowServes);

                if (nowServes != null)
                {
                    if (!string.Equals(c.HomeMarker, nowServes, StringComparison.Ordinal))
                        marks.Add(new HomeMark { ChestId = i, BucketKey = nowServes });
                }
                else if (hasMarker && !keep.Contains(i))
                {
                    marks.Add(new HomeMark { ChestId = i, BucketKey = null });   // clear
                }
            }
        }

        private static int CeilDiv(int a, int b) => b <= 0 ? a : (a + b - 1) / b;
    }
}
