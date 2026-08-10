using System;
using System.Collections.Generic;
using ChestButler.Core;

// Hand-rolled, dependency-free test runner for the pure Organize v2 allocator.
// Chests are addressed by their position in the input list (index == chest id in the plan output),
// which the allocator also treats as nearest-first ordering.
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(bool cond, string msg)
    {
        if (cond) { _passed++; Console.WriteLine("  PASS  " + msg); }
        else { _failed++; Console.WriteLine("  FAIL  " + msg); }
    }

    // ---- builders ---------------------------------------------------------------------------------

    private sealed class Stack
    {
        public string Norm;
        public int Count;
        public bool Stackable = true;
        public string Bucket;
    }

    private static Stack S(string norm, int count, string bucket, bool stackable = true)
        => new Stack { Norm = norm, Count = count, Bucket = bucket, Stackable = stackable };

    private static ChestView Chest(int id, Stack[] stacks = null, int totalSlots = 24,
        float distance = 0f, string uid = null,
        Dictionary<string, AnchorKind> anchors = null, string home = null,
        bool excludedTarget = false, bool excludedSource = false, int priority = 0)
    {
        var sv = new List<StackView>();
        if (stacks != null)
            foreach (var s in stacks)
                sv.Add(new StackView { Norm = s.Norm, Count = s.Count, Stackable = s.Stackable, BucketKey = s.Bucket });

        return new ChestView
        {
            Id = id,
            UidKey = uid ?? ("uid" + id.ToString("000")),
            Distance = distance == 0f ? id : distance,
            TotalSlots = totalSlots,
            Priority = priority,
            Stacks = sv,
            ExcludedAsTarget = excludedTarget,
            ExcludedAsSource = excludedSource,
            Anchors = anchors,
            HomeMarker = home,
        };
    }

    private static Dictionary<string, AnchorKind> Anchor(string bucket, AnchorKind kind)
        => new Dictionary<string, AnchorKind> { { bucket, kind } };

    private static PlannerInput In(List<ChestView> chests, int maxStack = 50, int miscPromote = 24,
        Dictionary<string, int> maxStackOf = null, Func<string, int> rank = null)
        => new PlannerInput
        {
            Chests = chests,
            MaxStackOf = n => maxStackOf != null && maxStackOf.TryGetValue(n, out var v) ? v : maxStack,
            BucketRank = rank ?? (_ => 0),
            DistanceBetween = (a, b) => Math.Abs(chests[a].Distance - chests[b].Distance),
            MiscPromoteSlots = miscPromote,
        };

    /// <summary>`count` one-of-a-kind unstackable weapons (distinct norms, 1 slot each), plus any
    /// extra stacks — the standard way to build a bucket whose demand is exactly `count` slots.</summary>
    private static Stack[] Gear(int from, int count, params Stack[] extra)
    {
        var list = new List<Stack>(extra);
        for (int i = 0; i < count; i++)
            list.Add(S("weapon" + (from + i).ToString("000"), 1, BucketKeys.Weapons, stackable: false));
        return list.ToArray();
    }

    // ---- assertions ------------------------------------------------------------------------------

    private static int MovesTo(List<OrganizeMove> moves, int tgt)
    {
        int n = 0;
        foreach (var m in moves) if (m.TgtId == tgt) n++;
        return n;
    }

    private static int TotalTo(List<OrganizeMove> moves, int tgt, string norm = null)
    {
        int n = 0;
        foreach (var m in moves) if (m.TgtId == tgt && (norm == null || m.Norm == norm)) n += m.Amount;
        return n;
    }

    private static int TotalFrom(List<OrganizeMove> moves, int src)
    {
        int n = 0;
        foreach (var m in moves) if (m.SrcId == src) n += m.Amount;
        return n;
    }

    private static bool AnySelfMove(List<OrganizeMove> moves)
    {
        foreach (var m in moves) if (m.SrcId == m.TgtId) return true;
        return false;
    }

    private static HashSet<int> Targets(List<OrganizeMove> moves)
    {
        var s = new HashSet<int>();
        foreach (var m in moves) s.Add(m.TgtId);
        return s;
    }

    /// <summary>Apply a plan to the census so the next run sees the world the plan intended — the only
    /// honest way to test re-run stability. Also applies the psort_home markers, because those are
    /// exactly what makes run 2 recognise run 1's claims (v2 plan §4.1).</summary>
    internal static void Apply(List<ChestView> chests, PlannerResult plan, bool applyHomes = true)
    {
        // Snapshot each stack identity's bucket/stackability BEFORE mutating anything: a move can
        // empty its source stack, and reading the prototype afterwards would lose the bucket key.
        var proto = new Dictionary<string, StackView>();
        foreach (var c in chests)
            foreach (var s in c.Stacks)
                if (!proto.ContainsKey(s.Norm)) proto[s.Norm] = s;

        foreach (var m in plan.Moves)
        {
            var src = chests[m.SrcId];
            int left = m.Amount;
            for (int i = 0; i < src.Stacks.Count && left > 0; i++)
            {
                var sv = src.Stacks[i];
                if (sv.Norm != m.Norm) continue;
                int take = Math.Min(sv.Count, left);
                sv.Count -= take;
                left -= take;
                src.Stacks[i] = sv;
            }
            src.Stacks.RemoveAll(s => s.Count <= 0);

            var tgt = chests[m.TgtId];
            bool merged = false;
            for (int i = 0; i < tgt.Stacks.Count; i++)
            {
                var sv = tgt.Stacks[i];
                if (sv.Norm != m.Norm) continue;
                sv.Count += m.Amount;
                tgt.Stacks[i] = sv;
                merged = true;
                break;
            }
            if (!merged)
            {
                proto.TryGetValue(m.Norm, out var p);
                tgt.Stacks.Add(new StackView
                {
                    Norm = m.Norm,
                    Count = m.Amount,
                    Stackable = p.Stackable,
                    BucketKey = p.BucketKey,
                });
            }
        }

        if (!applyHomes) return;

        foreach (var hm in plan.HomeMarks)
        {
            var c = chests[hm.ChestId];
            c.HomeMarker = hm.BucketKey;
            if (hm.BucketKey == null)
            {
                // A cleared psort_home also stops being a Home anchor next run — the adapter derives
                // the anchor FROM the marker, so the two must move together or the fuzz lies.
                if (c.Anchors != null)
                {
                    var drop = new List<string>();
                    foreach (var kv in c.Anchors) if (kv.Value == AnchorKind.Home) drop.Add(kv.Key);
                    foreach (var k in drop) c.Anchors.Remove(k);
                    if (c.Anchors.Count == 0) c.Anchors = null;
                }
                continue;
            }
            if (c.Anchors == null) c.Anchors = new Dictionary<string, AnchorKind>(StringComparer.Ordinal);
            // ONE psort_home value per chest -> ONE Home anchor: an overwrite drops the previous
            // Home-derived anchor too, exactly as the adapter re-derives it from the marker. This
            // harness once kept both, which is impossible in-game and made the fuzz report phantom
            // press-2 failures against the earlier incumbent-adoption attempts.
            var stale = new List<string>();
            foreach (var kv in c.Anchors)
                if (kv.Value == AnchorKind.Home
                    && !string.Equals(kv.Key, hm.BucketKey, StringComparison.Ordinal))
                    stale.Add(kv.Key);
            foreach (var k in stale) c.Anchors.Remove(k);
            c.Anchors[hm.BucketKey] = AnchorKind.Home;
        }
    }

    /// <summary>Per-norm item totals across every chest — the conservation invariant's currency.</summary>
    internal static Dictionary<string, int> Totals(List<ChestView> chests)
    {
        var t = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in chests)
            foreach (var s in c.Stacks)
                t[s.Norm] = (t.TryGetValue(s.Norm, out var v) ? v : 0) + s.Count;
        return t;
    }

    internal static bool SameTotals(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
            if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
        return true;
    }

    private static int Main()
    {
        Console.WriteLine("Organize v2 allocator unit tests");
        Console.WriteLine("================================");

        // 1) Empty input -> empty plan.
        Console.WriteLine("[1] empty input -> empty plan");
        {
            var r = OrganizePlanner.Plan(In(new List<ChestView>()));
            Check(r.Moves.Count == 0, "no moves");
            Check(r.Summary.TotalItems == 0 && r.Summary.TargetChests == 0, "zero summary");
            Check(OrganizePlanner.Plan(null).Moves.Count == 0, "null input is safe");
        }

        // 2) Volume sizing: slot demand is ceil(count / maxStack), summed per type.
        Console.WriteLine("[2] volume sizing drives how many chests a bucket claims");
        {
            // The source is a Sorter chest (target-excluded) so the ONLY capacity in the base is the
            // 1-slot anchor. Otherwise the source is itself claimable and the items simply stay put,
            // which is correct behaviour but tests nothing about sizing.

            // 50 wood at maxStack 50 = 1 slot -> fits the 1-slot anchor exactly.
            var one = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 50, "wood") }, excludedTarget: true),
                Chest(1, totalSlots: 1, anchors: Anchor("wood", AnchorKind.Sign)),
            };
            var r1 = OrganizePlanner.Plan(In(one));
            Check(TotalTo(r1.Moves, 1) == 50, "50 wood (1 slot) all lands in the 1-slot anchor");
            Check(r1.Summary.HomelessItems == 0, "nothing homeless");

            // 51 wood needs 2 slots, so 1 slot can only take 50 and the rest has nowhere to go.
            var two = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 51, "wood") }, excludedTarget: true),
                Chest(1, totalSlots: 1, anchors: Anchor("wood", AnchorKind.Sign)),
            };
            var r2 = OrganizePlanner.Plan(In(two));
            Check(TotalTo(r2.Moves, 1) == 50, "51 wood -> only 50 fits one slot");
            Check(r2.Summary.HomelessItems == 1, "the 51st is reported homeless, not lost");
        }

        // 3) A big category spans several chests; a small one shares.
        Console.WriteLine("[3] big bucket claims multiple chests, volume-adjusted");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 2000, "wood") }, totalSlots: 24),
                Chest(1, totalSlots: 24),
                Chest(2, totalSlots: 24),
                Chest(3, totalSlots: 24),
            };
            var r = OrganizePlanner.Plan(In(chests));
            // 2000 wood at 50/stack = 40 slots. Chest 0 holds it and is itself claimable, so the
            // allocator needs 40 slots across the pool.
            Check(r.Summary.HomelessItems == 0, "2000 wood finds room across the pool");
            Check(Targets(r.Moves).Count >= 1, "wood spans more than the one chest it started in");
            Check(!AnySelfMove(r.Moves), "no self-moves");
        }

        // 4) Anchors are used first and never repurposed; foreign items are evicted.
        Console.WriteLine("[4] anchors first, never repurposed, foreign items evicted");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("iron", 40, "metals"), S("carrot", 10, "cooking") },
                      anchors: Anchor("metals", AnchorKind.Pin), totalSlots: 24),
                Chest(1, new[] { S("iron", 5, "metals") }, totalSlots: 24),
                Chest(2, totalSlots: 24, anchors: Anchor("cooking", AnchorKind.Station)),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 0, "iron") == 5, "iron consolidates into its pinned anchor");
            Check(TotalTo(r.Moves, 2, "carrot") == 10, "the carrots are evicted to the cooking station chest");
            Check(TotalFrom(r.Moves, 2) == 0, "the station anchor is not drained");
        }

        // 5) Gear buckets are three separate homes (keys supplied as the adapter would).
        Console.WriteLine("[5] gear splits into weapons / armor / tools");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[]
                {
                    S("bronzesword", 1, BucketKeys.Weapons, stackable: false),
                    S("bronzehelmet", 1, BucketKeys.Armor, stackable: false),
                    S("hoe", 1, BucketKeys.Tools, stackable: false),
                }, totalSlots: 3),
                Chest(1, totalSlots: 24),
                Chest(2, totalSlots: 24),
                Chest(3, totalSlots: 24),
            };
            var r = OrganizePlanner.Plan(In(chests, maxStack: 1));
            Check(r.Summary.HomelessItems == 0, "all three pieces are placed");

            // One of the three legitimately stays where it is (that chest becomes its bucket's home),
            // so what matters is the FINAL layout: three gear buckets, three different chests.
            Apply(chests, r);
            var where = new Dictionary<string, int>();
            for (int ci = 0; ci < chests.Count; ci++)
                foreach (var s in chests[ci].Stacks)
                    if (BucketKeys.IsGear(s.BucketKey)) where[s.BucketKey] = ci;
            Check(where.Count == 3, "all three gear buckets still exist after the move");
            var distinct = new HashSet<int>(where.Values);
            Check(distinct.Count == 3, "weapons, armor and tools each end up in a different chest");
        }

        // 6) Ungrouped stackables: small ones share `misc`, a big one earns its own bucket. §16.4.1
        Console.WriteLine("[6] misc catch-all vs promoted per-type bucket");
        {
            var small = new List<ChestView>
            {
                Chest(0, new[] { S("resin", 12, BucketKeys.ForType("resin")),
                                 S("queenbee", 3, BucketKeys.ForType("queenbee")),
                                 S("wisp", 1, BucketKeys.ForType("wisp")) }, totalSlots: 24),
                Chest(1, totalSlots: 24),
                Chest(2, totalSlots: 24),
                Chest(3, totalSlots: 24),
            };
            var r = OrganizePlanner.Plan(In(small));
            Check(Targets(r.Moves).Count <= 1,
                "16 items of three tiny types share ONE chest, not three (§16.4.1)");

            // 2000 resin needs 40 slots — well over the 24-slot threshold — so it earns its own bucket
            // and is not squeezed in beside the oddments.
            var big = new List<ChestView>
            {
                Chest(0, new[] { S("resin", 2000, BucketKeys.ForType("resin")) }, totalSlots: 24),
                Chest(1, totalSlots: 24),
                Chest(2, totalSlots: 24),
            };
            var rb = OrganizePlanner.Plan(In(big));
            Check(rb.Summary.HomelessItems == 0, "a promoted per-type bucket gets the chests it needs");
        }

        // 7) Under-provisioned base: packs to capacity, reports the rest, loses nothing.
        Console.WriteLine("[7] under-provisioned base loses nothing");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 500, "wood") }, totalSlots: 4),
                Chest(1, totalSlots: 2, anchors: Anchor("wood", AnchorKind.Sign)),
            };
            var before = Totals(chests);
            var r = OrganizePlanner.Plan(In(chests));
            // Capacity is 4 + 2 = 6 slots = 300 items; the rest stays put and is reported.
            Check(r.Summary.HomelessItems == 200, "200 items reported as having no room");
            // Real conservation: apply the plan and compare per-norm totals across ALL chests.
            // Fails if DistributeAndDiff ever double-counts, invents or destroys an item.
            Apply(chests, r);
            Check(SameTotals(before, Totals(chests)),
                "conservation: per-norm totals identical after applying the plan");
            Check(r.Summary.HomelessItems + 300 == 500, "every item is either placed or reported");
        }

        // 8) `sort: off` is neither a source nor a target. DoD + §16.4.5.
        Console.WriteLine("[8] Ignore chests are neither source nor target");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, excludedTarget: true, excludedSource: true),
                Chest(1, new[] { S("wood", 10, "wood") }, totalSlots: 24),
                Chest(2, totalSlots: 24, anchors: Anchor("wood", AnchorKind.Sign)),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalFrom(r.Moves, 0) == 0, "nothing is taken OUT of the sort:off chest (the v1 bug)");
            Check(TotalTo(r.Moves, 0) == 0, "nothing is put INTO the sort:off chest");
            Check(TotalTo(r.Moves, 2) == 10, "the unprotected wood still organizes normally");
        }

        // 9) Manual/Sorter chests stay sources but are never targets. Keeps v1 test [7]'s meaning.
        Console.WriteLine("[9] Manual and Sorter chests are drained but never filled");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, excludedTarget: true),
                Chest(1, totalSlots: 24, anchors: Anchor("wood", AnchorKind.Sign)),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalFrom(r.Moves, 0) == 100 && TotalTo(r.Moves, 1) == 100,
                "wood flows OUT of the excluded chest into the anchor");
            Check(TotalTo(r.Moves, 0) == 0, "nothing is routed into it");
        }

        // 10) NG+ unmergeable stacks must not be over-budgeted. §16.5(a).
        //     Three 20-count stacks of the same item at different world levels cannot merge, so the
        //     adapter gives them distinct identities and each needs its OWN slot. One empty slot in the
        //     target therefore takes exactly one stack — not 50 items' worth spread across three.
        Console.WriteLine("[10] NG+ mixed-worldLevel stacks are not over-budgeted");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[]
                {
                    S("iron#1", 20, "metals"),
                    S("iron#2", 20, "metals"),
                    S("iron#3", 20, "metals"),
                }, totalSlots: 3, excludedTarget: true),
                Chest(1, totalSlots: 1, anchors: Anchor("metals", AnchorKind.Sign)),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(r.Moves.Count == 1, "exactly one move: one unmergeable stack per free slot");
            Check(TotalTo(r.Moves, 1) == 20, "20 items moved, not 50 - the slot holds one stack only");
            Check(r.Summary.HomelessItems == 40, "the other two stacks are reported, not re-queued forever");

            // Contrast: the SAME 60 items as one mergeable pile do pack 50 into that slot. This is the
            // difference the merge key buys, and the reason a naive planner over-budgets NG+ stacks.
            var mergeable = new List<ChestView>
            {
                Chest(0, new[] { S("iron", 60, "metals") }, totalSlots: 3, excludedTarget: true),
                Chest(1, totalSlots: 1, anchors: Anchor("metals", AnchorKind.Sign)),
            };
            var rm = OrganizePlanner.Plan(In(mergeable));
            Check(TotalTo(rm.Moves, 1) == 50, "one mergeable 60-pile fills the slot to maxStack (50)");
        }

        // 11) THE ACCEPTANCE TEST: a second Organize on an organized base moves zero items.
        Console.WriteLine("[11] re-run stability - the acceptance test (§12 / §4.1)");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 300, "wood"), S("iron", 40, "metals") }, totalSlots: 24),
                Chest(1, new[] { S("wood", 120, "wood") }, totalSlots: 24),
                Chest(2, totalSlots: 24),
                Chest(3, totalSlots: 24),
                Chest(4, totalSlots: 24, anchors: Anchor("metals", AnchorKind.Station)),
            };
            var first = OrganizePlanner.Plan(In(chests));
            Check(first.Moves.Count > 0, "run 1 on a messy base does work");
            Check(first.HomeMarks.Count > 0, "run 1 records at least one claimed home (psort_home)");

            Apply(chests, first);

            var second = OrganizePlanner.Plan(In(chests));
            Check(second.Moves.Count == 0,
                "run 2 moves ZERO items - without psort_home this relocates the whole spill forever");
            Check(second.Summary.TotalItems == 0, "and the summary agrees");

            var third = OrganizePlanner.Plan(In(chests));
            Check(third.Moves.Count == 0, "run 3 is also stable");

            // NEGATIVE CONTROL — is psort_home actually load-bearing, or does test [11] pass anyway?
            //
            // Worth being precise about, because it is NOT load-bearing for the trivial case. This
            // allocator claims chests by (will-be-empty, distance, uid), which is a pure function of
            // the base's geometry rather than of what the chests hold right now, so pressing Organize
            // twice with nothing else changing converges even with the marker discarded. §16.1's worked
            // example assumed the preference test was "is this chest empty AT THIS MOMENT", which does
            // flip between runs.
            //
            // Where the marker earns its keep is when the census CHANGES. Buckets are allocated
            // largest-demand-first, so a new, bigger category will claim the nearest chests — including
            // the ones another bucket is already using. Below: wood settles first, then the player mines
            // 2,000 stone. With the markers, wood's chests are anchors and stone must go elsewhere.
            Func<List<ChestView>> baseWithWood = () => new List<ChestView>
            {
                Chest(0, new[] { S("wood", 420, "wood") }, totalSlots: 24, excludedTarget: true),
                Chest(1, totalSlots: 24),
                Chest(2, totalSlots: 24),
                Chest(3, totalSlots: 24),
                Chest(4, totalSlots: 24),
            };

            // -- with markers --
            var kept = baseWithWood();
            Apply(kept, OrganizePlanner.Plan(In(kept)), applyHomes: true);
            kept[0].Stacks.Add(new StackView { Norm = "stone", Count = 2000, Stackable = true, BucketKey = "stone" });
            var keptRun = OrganizePlanner.Plan(In(kept));
            int woodMovedKept = 0;
            foreach (var m in keptRun.Moves) if (m.Norm == "wood") woodMovedKept += m.Amount;

            // -- markers discarded --
            // Since 2.0's incumbent adoption this no longer displaces the wood either: the wood
            // bucket has no anchor left, so adoption re-derives its home from where it already
            // sits and re-fences the chest before stone allocates. That is the owner's ore-chest
            // complaint in miniature, closed by design rather than by the marker surviving.
            var lost = baseWithWood();
            Apply(lost, OrganizePlanner.Plan(In(lost)), applyHomes: false);
            lost[0].Stacks.Add(new StackView { Norm = "stone", Count = 2000, Stackable = true, BucketKey = "stone" });
            var lostRun = OrganizePlanner.Plan(In(lost));
            int woodMovedLost = 0;
            foreach (var m in lostRun.Moves) if (m.Norm == "wood") woodMovedLost += m.Amount;

            Check(woodMovedKept == 0,
                "WITH psort_home, a new bigger category does not displace the settled wood (moved " +
                woodMovedKept + ")");
            Check(woodMovedLost == 0,
                "with the marker LOST, incumbent adoption re-derives the same home (moved " +
                woodMovedLost + ")");

            // So where is the marker still load-bearing now that adoption exists? DIRECTION under
            // contest. Adoption follows quantity (the chest holding most wins); the marker follows
            // history. When a bigger fresh pile appears somewhere else, only the marker keeps the
            // established chest as the home the pile consolidates INTO — drop it, and adoption
            // crowns the bigger pile's chest instead. Same convergence either way; the marker
            // decides which chest the player finds the wood in.
            var dirKept = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24, distance: 1f,
                      home: "wood", anchors: Anchor("wood", AnchorKind.Home)),
                Chest(1, new[] { S("wood", 300, "wood") }, totalSlots: 24, distance: 2f),
                Chest(2, totalSlots: 24, distance: 3f),
            };
            var rDirKept = OrganizePlanner.Plan(In(dirKept));
            Check(TotalTo(rDirKept.Moves, 0, "wood") == 300 && TotalFrom(rDirKept.Moves, 0) == 0,
                "the MARKED home wins: the bigger new pile consolidates INTO it (marker load-bearing)");

            var dirLost = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24, distance: 1f),
                Chest(1, new[] { S("wood", 300, "wood") }, totalSlots: 24, distance: 2f),
                Chest(2, totalSlots: 24, distance: 3f),
            };
            var rDirLost = OrganizePlanner.Plan(In(dirLost));
            Check(TotalTo(rDirLost.Moves, 1, "wood") == 100 && TotalFrom(rDirLost.Moves, 1) == 0,
                "unmarked, adoption crowns the chest with the larger pile instead");
        }

        // 12) A claimed home survives as an anchor, and a dead marker is released.
        Console.WriteLine("[12] psort_home is honoured, and released when no longer needed");
        {
            // The marker is the ONLY thing making chest 2 wood's home. It must be preferred over the
            // equally-empty, equally-distant chest 3.
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24),
                Chest(1, totalSlots: 24),
                Chest(2, totalSlots: 24, home: "wood", anchors: Anchor("wood", AnchorKind.Home)),
                Chest(3, totalSlots: 24),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 2) == 100, "wood goes to the chest already marked as its home");

            bool rewrote = false;
            foreach (var hm in r.HomeMarks) if (hm.ChestId == 2) rewrote = true;
            Check(!rewrote, "an already-correct marker is not rewritten");

            // A marker for a bucket with nothing left in the base is stale and must be cleared,
            // or a base that shrinks keeps dead homes reserved forever.
            var stale = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 10, "wood") }, totalSlots: 24,
                      anchors: Anchor("wood", AnchorKind.Sign)),
                Chest(1, totalSlots: 24, home: "meads", anchors: Anchor("meads", AnchorKind.Home)),
            };
            var rs = OrganizePlanner.Plan(In(stale));
            bool cleared = false;
            foreach (var hm in rs.HomeMarks) if (hm.ChestId == 1 && hm.BucketKey == null) cleared = true;
            Check(cleared, "a marker whose bucket no longer exists is cleared (§4.1)");
        }

        // 13) Dead anchors do not remove a chest from the free pool. §16.4.6.
        Console.WriteLine("[13] a dead anchor does not reserve a chest");
        {
            // Chest 1 anchors `meads`, but there are no meads in the base at all. It must still be
            // claimable for wood rather than sitting idle forever.
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 1000, "wood") }, totalSlots: 4),
                Chest(1, totalSlots: 24, anchors: Anchor("meads", AnchorKind.Sign)),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 1) > 0, "the chest anchoring an empty bucket is still used for wood");

            // KILLS M18 the rest of the way: not only must the dead-anchor chest stay claimable
            // when it is the ONLY room left (above), it must not even be DEMOTED below a plain
            // chest. Counting a dead anchor as "shared" quietly re-ranks it last, so the wood
            // walks past the nearer chest for no reason the player can see (§16.4.6).
            var demoted = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24, distance: 0.1f,
                      excludedTarget: true),
                Chest(1, totalSlots: 24, distance: 1f, anchors: Anchor("meads", AnchorKind.Sign)),
                Chest(2, totalSlots: 24, distance: 2f),
            };
            var rDem = OrganizePlanner.Plan(In(demoted));
            Check(TotalTo(rDem.Moves, 1) == 100 && TotalTo(rDem.Moves, 2) == 0,
                "a dead anchor does not demote its chest either - the nearer chest still wins (M18)");
        }

        // 14) Determinism: identical input -> byte-identical plan; ties break on uid.
        Console.WriteLine("[14] determinism and uid tie-breaks");
        {
            // Source is target-excluded so the allocator must choose between the two EQUIDISTANT empty
            // chests — which is the tie the uid break exists for. A symmetric storage hall with chests
            // at +/-3.00 m is the normal way people build.
            Func<List<ChestView>> make = () => new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24, distance: 1f, uid: "zzz",
                      excludedTarget: true),
                Chest(1, totalSlots: 24, distance: 3f, uid: "bbb"),
                Chest(2, totalSlots: 24, distance: 3f, uid: "aaa"),   // same distance, lower uid
            };
            var a = OrganizePlanner.Plan(In(make()));
            var b = OrganizePlanner.Plan(In(make()));
            Check(a.Moves.Count == b.Moves.Count, "same move count");
            bool same = true;
            for (int i = 0; i < a.Moves.Count && i < b.Moves.Count; i++)
                if (a.Moves[i].SrcId != b.Moves[i].SrcId || a.Moves[i].TgtId != b.Moves[i].TgtId ||
                    a.Moves[i].Amount != b.Moves[i].Amount) same = false;
            Check(same, "identical plans from identical input");

            var targets = Targets(a.Moves);
            Check(targets.Contains(2) && !targets.Contains(1),
                "equidistant chests resolve on ZDO uid: 'aaa' wins over 'bbb'");
        }

        // 15) Evictions are emitted before fills for the same chest. §6 / §16.4.8.
        Console.WriteLine("[15] evict-before-fill ordering");
        {
            // Chest 1 is metals' anchor but currently holds wood; chest 2 is wood's anchor.
            // The wood must leave chest 1 before the iron is sent into it.
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("iron", 100, "metals") }, totalSlots: 24),
                Chest(1, new[] { S("wood", 100, "wood") }, totalSlots: 24,
                      anchors: Anchor("metals", AnchorKind.Pin)),
                Chest(2, totalSlots: 24, anchors: Anchor("wood", AnchorKind.Pin)),
            };
            var r = OrganizePlanner.Plan(In(chests));
            int firstOut = -1, firstIn = -1;
            for (int i = 0; i < r.Moves.Count; i++)
            {
                if (firstOut < 0 && r.Moves[i].SrcId == 1) firstOut = i;
                if (firstIn < 0 && r.Moves[i].TgtId == 1) firstIn = i;
            }
            Check(firstOut >= 0 && firstIn >= 0, "the plan both drains and fills chest 1");
            Check(firstOut < firstIn, "the eviction is ordered before the fill");
        }

        // 16) Group tables cannot drift apart. §16.4.3.
        Console.WriteLine("[16] GroupOrder covers every group, and vice versa");
        {
            foreach (var name in GroupTables.GroupOrder)
                Check(GroupTables.Defaults.ContainsKey(name),
                    "GroupOrder entry '" + name + "' is a real group");
            foreach (var kv in GroupTables.Defaults)
                Check(Array.IndexOf(GroupTables.GroupOrder, kv.Key) >= 0,
                    "group '" + kv.Key + "' appears in GroupOrder");
            Check(GroupTables.GroupOrder.Length == GroupTables.Defaults.Count,
                "no duplicates or omissions");

            // The overlap that actually exists in the shipped defaults, which GroupOrder resolves.
            Check(Names.Matches("*ore", "flametalore"), "FlametalOre matches the ores token");
            Check(Names.Matches("flametal*", "flametalore"), "FlametalOre also matches the metals token");
            Check(Array.IndexOf(GroupTables.GroupOrder, "metals") <
                  Array.IndexOf(GroupTables.GroupOrder, "ores"),
                "metals outranks ores, so FlametalOre files as a metal");
        }

        // 17) Bucket key formats — these are persisted in ZDOs, so the format is a contract.
        Console.WriteLine("[17] bucket key formats");
        {
            Check(BucketKeys.IsGear(BucketKeys.Weapons), "gear keys are recognised as gear");
            Check(!BucketKeys.IsGear("wood"), "a group key is not gear");
            Check(BucketKeys.IsPerType(BucketKeys.ForType("resin")), "per-type keys are recognised");
            Check(BucketKeys.TypeOf(BucketKeys.ForType("resin")) == "resin", "per-type key round-trips");
            Check(BucketKeys.TypeOf("wood") == null, "a group key has no per-type payload");
            Check(BucketKeys.Label(BucketKeys.Weapons) == "weapons", "gear label strips the prefix");
            Check(BucketKeys.Label("wood") == "wood", "group label is the group name");
            Check(!BucketKeys.IsPerType(BucketKeys.Misc), "misc is not a per-type bucket");
        }

        // 18) Names: locked-in matching semantics the item groups depend on (carried over from 1.1.2).
        Console.WriteLine("[18] name normalization and wildcard matching");
        {
            Check(Names.Normalize("$item_trophy_boar") == "trophyboar", "$item_ prefix and underscores stripped");
            Check(Names.Normalize("$piece_chest") == "piecechest", "bare $ prefix stripped");
            Check(Names.Normalize("") == "", "empty name is empty");
            Check(Names.Normalize("$item_TrophyBoar") == "trophyboar", "lowercased");
            Check(Names.Normalize("$item_trophy_boar") == "trophyboar", "repeat call hits the cache with the same result");

            Check(Names.Matches("wood", "wood"), "exact token matches");
            Check(!Names.Matches("wood", "finewood"), "exact token does not match a superstring");
            Check(Names.Matches("trophy*", "trophyboar"), "trailing wildcard is a prefix match");
            Check(!Names.Matches("trophy*", "boartrophy"), "trailing wildcard is not a suffix match");
            Check(Names.Matches("*meat", "boarmeat"), "leading wildcard is a suffix match");
            Check(Names.Matches("*mushroom*", "redmushroomstew"), "double wildcard is a contains match");
            Check(!Names.Matches("*", "anything"), "a bare '*' matches nothing (empty core)");
            Check(!Names.Matches("", "wood"), "empty token matches nothing");
            Check(!Names.Matches("wood", ""), "empty name matches nothing");
            Check(Names.Matches("pickaxe*", "pickaxeantler"), "the default ToolTokens pattern catches pickaxes");
        }

        // 19) Scale. Design target is a 400+ chest base (v2 plan §15/§16.3), where "any O(chests^2)
        //     pass or per-chest allocation in a hot path is a defect, not a nit". This times the PURE
        //     allocator; the Unity adapter's cost is logged by Organizer.BuildPlan in-game, which is
        //     the half that cannot be measured from here.
        Console.WriteLine("[19] scale: the pure allocator at the 400-chest design target");
        {
            const int chestCount = 400;
            var big = new List<ChestView>(chestCount);
            var groups = new[] { "wood", "metals", "ores", "stone", "cooking", "meat", "seeds",
                                 "trophies", "valuables", "meads", "ammo", "hides", "fuel" };
            var rnd = 12345;
            for (int i = 0; i < chestCount; i++)
            {
                // deterministic pseudo-shuffle: no Random, so the benchmark is reproducible
                rnd = (rnd * 1103515245 + 12345) & 0x7fffffff;
                var stacks = new List<Stack>();
                int kinds = rnd % 5;
                for (int k = 0; k <= kinds; k++)
                {
                    var g = groups[(rnd / (k + 1)) % groups.Length];
                    stacks.Add(S(g + "item" + ((rnd / (k + 3)) % 7), 20 + (rnd % 40), g));
                }
                big.Add(Chest(i, stacks.ToArray(), totalSlots: 24, distance: i * 0.5f));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = OrganizePlanner.Plan(In(big));
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine("        " + chestCount + " chests, " + r.Moves.Count + " moves, " +
                r.Summary.BucketsPlanned + " buckets, planned in " + ms.ToString("0.0") + " ms");
            Check(ms < 500, "allocation at 400 chests stays well under half a second (was " +
                ms.ToString("0.0") + " ms)");
            Check(r.Moves.Count > 0, "the benchmark base actually needed organizing");

            // Re-run stability has to hold at scale too, not just on a 5-chest toy.
            // The acceptance test has to hold at the design target, not just on a 5-chest toy.
            Apply(big, r);
            var pass2 = OrganizePlanner.Plan(In(big));
            Check(pass2.Moves.Count == 0,
                "a 400-chest base moves ZERO items on the second press (was " + pass2.Moves.Count + ")");
            Apply(big, pass2);
            var pass3 = OrganizePlanner.Plan(In(big));
            Check(pass3.Moves.Count == 0, "and stays settled on the third");
        }

        // 20) W2 — Gather shortfall arithmetic (docs/gather-plan.md §7).
        Console.WriteLine("[20] Gather: shortfall, craft multiplier, require-only-one");
        {
            Func<string, int, int, int, GatherNeed> N = (name, needed, inPlayer, inStorage) =>
                new GatherNeed { SharedName = name, Needed = needed, InPlayer = inPlayer, InStorage = inStorage };

            // Basic shortfall and the storage cap.
            var wood = N("$item_wood", 20, 5, 100);
            Check(wood.Shortfall == 15, "shortfall is needed minus what you carry");
            Check(wood.Gatherable == 15, "storage covers it fully");

            var scarce = N("$item_iron", 20, 5, 4);
            Check(scarce.Shortfall == 15 && scarce.Gatherable == 4, "gatherable is capped by storage");

            var plenty = N("$item_stone", 10, 30, 500);
            Check(plenty.Shortfall == 0 && plenty.Gatherable == 0, "already carrying enough -> nothing to fetch");

            var none = N("$item_tin", 10, 0, 0);
            Check(none.Gatherable == 0, "nothing in storage -> nothing to fetch");

            // Needed already folds in the craft multiplier at capture time; check the maths downstream
            // of a x5 craft: 3 per craft x5 = 15, carrying 4, so 11 short.
            var multi = N("$item_leather", 3 * 5, 4, 99);
            Check(multi.Shortfall == 11, "craft multiplier is reflected in the shortfall");

            // Normal recipe: every short ingredient storage can help with is returned.
            var normal = new List<GatherNeed> { wood, scarce, plenty, none };
            var rNormal = GatherMath.Resolve(normal, false);
            Check(rNormal.Count == 2, "a normal recipe gathers every coverable ingredient");
            Check(rNormal[0].SharedName == "$item_wood" && rNormal[1].SharedName == "$item_iron",
                "and keeps them in the panel's order");

            // require-only-one: exactly one ingredient, the one storage brings closest to done.
            var onlyOne = new List<GatherNeed>
            {
                N("$item_bronze", 10, 0, 3),    // still 7 short after gathering
                N("$item_iron",   10, 0, 9),    // only 1 short after gathering  <- best
                N("$item_silver", 10, 0, 1),    // 9 short
            };
            var rOne = GatherMath.Resolve(onlyOne, true);
            Check(rOne.Count == 1, "require-only-one fetches exactly one ingredient, not all three");
            Check(rOne[0].SharedName == "$item_iron", "and picks the one storage nearly satisfies");

            // If the player can already make it with something they carry, fetch nothing at all.
            var satisfied = new List<GatherNeed>
            {
                N("$item_bronze", 10, 0, 100),
                N("$item_iron",   10, 10, 100),   // already covered
            };
            Check(GatherMath.Resolve(satisfied, true).Count == 0,
                "require-only-one with an option already in hand gathers nothing");

            // Deterministic tie-break so the choice does not flicker between panel openings.
            var tie = new List<GatherNeed>
            {
                N("$item_zzz", 10, 0, 5),
                N("$item_aaa", 10, 0, 5),
            };
            var rTie = GatherMath.Resolve(tie, true);
            Check(rTie.Count == 1 && rTie[0].SharedName == "$item_aaa",
                "equal options resolve on name, stably");

            Check(GatherMath.Resolve(null, false).Count == 0, "null input is safe");
            Check(GatherMath.Resolve(new List<GatherNeed>(), true).Count == 0, "empty input is safe");
        }

        // 21) KILLS M13 — the reinstated free-pool veto (the shipped bug f628bed fixed): a chest
        //     anchored for a LIVE low-demand bucket holds the only remaining capacity. Its surplus
        //     must be usable as a last resort, not fenced off wholesale.
        Console.WriteLine("[21] a live station chest's surplus is a last resort, never fenced off");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 50, "wood") }, totalSlots: 24, distance: 1f,
                      anchors: Anchor("wood", AnchorKind.Station)),
                Chest(1, Gear(0, 24), totalSlots: 24, distance: 2f),
                Chest(2, Gear(24, 6), totalSlots: 24, distance: 3f, excludedTarget: true),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(r.Summary.HomelessItems == 0,
                "zero homeless: the 6 spare weapons use the station chest's idle slots (M13 fences them off)");
            Check(TotalTo(r.Moves, 0) > 0, "the station chest's surplus is actually used");
            Check(TotalFrom(r.Moves, 0) == 0, "the wood is not evicted from its own station chest");
        }

        // 22) KILLS M14 — vetoing ANY already-reserved chest. A realistic multi-bucket base where
        //     co-tenancy is REQUIRED: total demand (30 slots) exactly equals total capacity, so
        //     later buckets must share partially-reserved chests. Hand-computed expectation: 0.
        Console.WriteLine("[22] six buckets share tight capacity via co-tenancy (M14 starves four of them)");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[]
                {
                    S("wood", 600, "wood"),        // 12 slots
                    S("deerhide", 350, "hides"),   //  7
                    S("iron", 200, "metals"),      //  4
                    S("carrot", 150, "cooking"),   //  3
                    S("rawmeat", 100, "meat"),     //  2
                    S("seeds", 100, "seeds"),      //  2  -> 30 slots total demand
                }, totalSlots: 30, distance: 0.1f, excludedTarget: true),
                Chest(1, totalSlots: 10, distance: 1f, anchors: Anchor("metals", AnchorKind.Station)),
                Chest(2, totalSlots: 10, distance: 2f, anchors: Anchor("cooking", AnchorKind.Sign)),
                Chest(3, totalSlots: 10, distance: 3f),
            };
            var before = Totals(chests);
            var r = OrganizePlanner.Plan(In(chests));
            Check(r.Summary.HomelessItems == 0,
                "30 slots of demand fit exactly into 30 slots of capacity (M14 reports 650 homeless)");
            Check(TotalTo(r.Moves, 1, "iron") == 200, "the metals go to their station chest");
            Check(TotalTo(r.Moves, 2, "carrot") == 150, "the cooking goes to its sign chest");
            Apply(chests, r);
            Check(SameTotals(before, Totals(chests)), "conservation holds across the whole shuffle");
        }

        // 23) KILLS M11 — Claimed = true unconditionally. When bucket B co-tenants into a chest that
        //     is bucket A's STATION anchor, no psort_home may be written there: that marker would make
        //     the chest AnchorKind.Home for B next run and push A out of its own chest.
        Console.WriteLine("[23] marker exactness: co-tenancy writes NO psort_home on a foreign chest");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 50, "wood") }, totalSlots: 24, distance: 1f,
                      anchors: Anchor("wood", AnchorKind.Station)),
                Chest(1, Gear(0, 24), totalSlots: 24, distance: 2f),
                Chest(2, Gear(24, 6), totalSlots: 24, distance: 3f, excludedTarget: true),
            };
            var r = OrganizePlanner.Plan(In(chests));
            // weapons claim chest 1 outright (marker earned) and co-tenant into chest 0 (no marker).
            Check(r.HomeMarks.Count == 1, "exactly ONE psort_home write in the whole plan");
            Check(r.HomeMarks.Count == 1 && r.HomeMarks[0].ChestId == 1
                  && r.HomeMarks[0].BucketKey == BucketKeys.Weapons,
                "and it is the weapons' own claimed chest - never the co-tenanted station chest");
        }

        // 24) KILLS M09 — dropping the `kind < AnchorKind.Station` skip. A psort_home Home anchor is
        //     a phase-2 memo, never a phase-1 instruction: the Station bucket gets the chest's slots
        //     in phase 1 even when the Home bucket has LARGER demand and would otherwise run first.
        Console.WriteLine("[24] phase-1 gating: a Home anchor never outruns a Station anchor");
        {
            var chests = new List<ChestView>
            {
                Chest(0, totalSlots: 4, distance: 1f, home: "wood",
                      anchors: new Dictionary<string, AnchorKind>
                      { { "metals", AnchorKind.Station }, { "wood", AnchorKind.Home } }),
                Chest(1, totalSlots: 24, distance: 2f),
                Chest(2, new[] { S("wood", 300, "wood"), S("iron", 200, "metals") },
                      totalSlots: 24, distance: 0.1f, excludedTarget: true),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 0, "iron") == 200,
                "the station bucket gets its chest in phase 1 (M09 lets the bigger Home bucket steal it)");
            Check(TotalTo(r.Moves, 0, "wood") == 0, "no wood lands in the station chest");
            Check(TotalTo(r.Moves, 1, "wood") == 300, "the Home bucket tops up from the free pool in phase 2");
        }

        // 25) KILLS M30 — CompareAnchors ignoring AnchorKind. One bucket with Pin, Sign, Station and
        //     Home chests and demand for only 6 of the 12 phase-1 slots: the fill order must be
        //     Pin > Sign > Station > Home even though distance ranks them exactly the other way.
        Console.WriteLine("[25] anchor strength IS the fill order: Pin > Sign > Station > Home");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 300, "wood") }, totalSlots: 24, distance: 5f,
                      excludedTarget: true),
                Chest(1, totalSlots: 4, distance: 1f, anchors: Anchor("wood", AnchorKind.Station)),
                Chest(2, totalSlots: 4, distance: 2f, anchors: Anchor("wood", AnchorKind.Sign)),
                Chest(3, totalSlots: 4, distance: 3f, anchors: Anchor("wood", AnchorKind.Pin)),
                Chest(4, totalSlots: 4, distance: 0.5f, home: "wood",
                      anchors: Anchor("wood", AnchorKind.Home)),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 3) == 200, "the PINNED chest fills first (200 of 300)");
            Check(TotalTo(r.Moves, 2) == 100, "the SIGN chest takes the remainder");
            Check(TotalTo(r.Moves, 1) == 0, "the nearer station chest gets nothing");
            Check(TotalTo(r.Moves, 4) == 0, "the nearest Home chest gets nothing - Home is weakest");
        }

        // 26) KILLS M04 / M15 / M05 — the free-pick preference chain, one key per assertion.
        Console.WriteLine("[26] free-chest preference: wholly-empty, then nearest, then uid");
        {
            // (a) WHOLLY-empty beats immovable-cluttered, even when the cluttered chest is nearer.
            //     M04 prefers the cluttered one outright; M15 calls both 'empty' and lets distance pick
            //     the cluttered one. Real bug: sorted items wedged between someone's locked stacks.
            var a = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24, distance: 0.1f,
                      excludedTarget: true),
                Chest(1, new[] { S("junk1", 10, null), S("junk2", 10, null), S("junk3", 10, null),
                                 S("junk4", 10, null) }, totalSlots: 24, distance: 1f),
                Chest(2, totalSlots: 24, distance: 2f),
            };
            var ra = OrganizePlanner.Plan(In(a));
            Check(TotalTo(ra.Moves, 2) == 100 && TotalTo(ra.Moves, 1) == 0,
                "the wholly-empty chest wins over the nearer immovable-cluttered one (M04/M15)");

            // (b) nearest beats farthest at equal emptiness — M05 reverses the comparison. The uids
            //     oppose the distances so a uid-first picker cannot pass by accident.
            var b = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24, distance: 0.1f,
                      uid: "mmm", excludedTarget: true),
                Chest(1, totalSlots: 24, distance: 1f, uid: "zzz"),
                Chest(2, totalSlots: 24, distance: 5f, uid: "aaa"),
            };
            var rb = OrganizePlanner.Plan(In(b));
            Check(TotalTo(rb.Moves, 1) == 100 && TotalTo(rb.Moves, 2) == 0,
                "the nearest empty chest wins (M05 sends the wood across the base)");

            // (c) at an exact tie, the lower ZDO uid wins - stable across sessions.
            var c = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 24, distance: 0.1f,
                      uid: "mmm", excludedTarget: true),
                Chest(1, totalSlots: 24, distance: 3f, uid: "bbb"),
                Chest(2, totalSlots: 24, distance: 3f, uid: "aaa"),
            };
            var rc = OrganizePlanner.Plan(In(c));
            Check(TotalTo(rc.Moves, 2) == 100 && TotalTo(rc.Moves, 1) == 0,
                "an exact distance tie resolves on uid: 'aaa' beats 'bbb'");
        }

        // 27) KILLS M10 / M22 / M20 — ledger honesty.
        Console.WriteLine("[27] the slot ledger never over-promises and never over-reserves");
        {
            // (a) M10, the §16.4.2 over-reserve: a shared station chest serves two small buckets.
            //     If the first taker grabs ALL its slots instead of min(slotsLeft, needed), the wood
            //     is starved and 1100 items go homeless in a base that fits exactly.
            var a = new List<ChestView>
            {
                Chest(0, totalSlots: 24, distance: 1f,
                      anchors: new Dictionary<string, AnchorKind>
                      { { "cooking", AnchorKind.Station }, { "meat", AnchorKind.Station } }),
                Chest(1, totalSlots: 20, distance: 2f),
                Chest(2, new[] { S("cookedmeat", 100, "cooking"), S("rawmeat", 100, "meat"),
                                 S("wood", 2000, "wood") },
                      totalSlots: 44, distance: 0.1f, excludedTarget: true),
            };
            var ra = OrganizePlanner.Plan(In(a));
            Check(ra.Summary.HomelessItems == 0,
                "44 slots of demand fit 44 slots of capacity - anchors take only what they need (M10)");

            // (b) M22: immovable stacks permanently cost slots. 4-slot chest, 2 slots locked by
            //     immovables -> the plan MUST report 100 homeless rather than promise room that
            //     does not exist and let execution fail.
            var b = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 200, "wood") }, totalSlots: 4, distance: 0.1f,
                      excludedTarget: true),
                Chest(1, new[] { S("junk1", 10, null), S("junk2", 10, null) },
                      totalSlots: 4, distance: 1f),
            };
            var rb = OrganizePlanner.Plan(In(b));
            Check(rb.Summary.HomelessItems == 100,
                "immovable stacks reduce capacity: 100 wood honestly reported homeless (M22 promises 0)");
            Check(TotalTo(rb.Moves, 1) == 100, "and only the 2 genuinely free slots are filled");

            // (c) M20, the CeilDiv boundary: counts exactly divisible by maxStack. (100+50)/50 = 3
            //     slots instead of 2 makes the first bucket over-reserve the shared sign chest and
            //     starve the second in a base that fits exactly.
            var c = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood"), S("stone", 100, "stone") },
                      totalSlots: 4, distance: 0.1f, excludedTarget: true),
                Chest(1, totalSlots: 4, distance: 1f,
                      anchors: new Dictionary<string, AnchorKind>
                      { { "wood", AnchorKind.Sign }, { "stone", AnchorKind.Sign } }),
            };
            var rcd = OrganizePlanner.Plan(In(c));
            Check(rcd.Summary.HomelessItems == 0,
                "counts exactly divisible by maxStack cost exactly count/maxStack slots (M20 starves one bucket)");
            Check(TotalTo(rcd.Moves, 1) == 200, "all 200 items land in the 4-slot sign chest");

            // (d) the other CeilDiv boundary: a single item needs a whole slot, no less.
            var d = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 1, "wood") }, totalSlots: 4, distance: 0.1f,
                      excludedTarget: true),
                Chest(1, totalSlots: 1, distance: 1f, anchors: Anchor("wood", AnchorKind.Sign)),
            };
            var rd = OrganizePlanner.Plan(In(d));
            Check(TotalTo(rd.Moves, 1) == 1 && rd.Summary.HomelessItems == 0,
                "count=1 demands one slot and is placed");
        }

        // 28) KILLS M18 — dropping the dead-anchor filter. A stale psort_home for a bucket with ZERO
        //     items must not stop a live bucket claiming the chest, and the run must overwrite the
        //     stale marker with the live claimant's (which is how the stale value gets cleaned up).
        Console.WriteLine("[28] dead-anchor hygiene: a stale psort_home neither reserves nor survives");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 1000, "wood") }, totalSlots: 24, distance: 0.1f,
                      excludedTarget: true),
                Chest(1, totalSlots: 24, distance: 1f, home: "meads",
                      anchors: Anchor("meads", AnchorKind.Home)),   // no meads exist anywhere
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 1) == 1000, "the chest with the dead marker is claimed by the live bucket");
            Check(r.HomeMarks.Count == 1 && r.HomeMarks[0].ChestId == 1
                  && r.HomeMarks[0].BucketKey == "wood",
                "the stale 'meads' marker is replaced by the claimant's own psort_home (M18 leaves no write)");
        }

        // 29) KILLS M24 — demand + 1 per bucket. A bucket whose demand exactly fills its anchor must
        //     reserve exactly that and touch NO free chest: an off-by-one claim leaks a psort_home
        //     marker onto a chest the bucket does not need.
        Console.WriteLine("[29] demand exactness: an exact fit claims nothing extra");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood") }, totalSlots: 4, distance: 0.1f,
                      excludedTarget: true),
                Chest(1, totalSlots: 2, distance: 1f, anchors: Anchor("wood", AnchorKind.Sign)),
                Chest(2, totalSlots: 24, distance: 2f),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 1) == 100 && r.Summary.HomelessItems == 0,
                "100 wood (exactly 2 slots) fill the 2-slot anchor");
            Check(TotalTo(r.Moves, 2) == 0, "the free chest receives nothing");
            Check(r.HomeMarks.Count == 0,
                "and is not claimed either - no psort_home anywhere (M24's +1 slot claims it)");
        }

        // 30) KILLS M27 — bucket order by string hash. Two parts, both deterministic in-process:
        //     (a) the same logical base with every stack list reversed (which permutes the bucket
        //         first-seen order) must produce the identical normalized plan;
        //     (b) an equal-demand tie must resolve by BucketRank in BOTH directions - a hash-order
        //         comparator picks the same winner regardless of rank, so it cannot pass both.
        Console.WriteLine("[30] determinism: hash-order iteration cannot reproduce the bucket contract");
        {
            Func<bool, List<ChestView>> mk = reversed =>
            {
                var stacks0 = new List<Stack>
                {
                    S("wood", 90, "wood"), S("iron", 70, "metals"), S("carrot", 50, "cooking"),
                    S("rawmeat", 30, "meat"), S("coal", 30, "fuel"),
                };
                var stacks1 = new List<Stack>
                {
                    S("stone", 90, "stone"), S("wood", 60, "wood"), S("deerhide", 40, "hides"),
                };
                if (reversed) { stacks0.Reverse(); stacks1.Reverse(); }
                return new List<ChestView>
                {
                    Chest(0, stacks0.ToArray(), totalSlots: 24, distance: 1f),
                    Chest(1, stacks1.ToArray(), totalSlots: 24, distance: 2f),
                    Chest(2, totalSlots: 24, distance: 3f),
                    Chest(3, totalSlots: 24, distance: 4f, anchors: Anchor("metals", AnchorKind.Station)),
                };
            };
            Func<List<OrganizeMove>, List<string>> normalize = moves =>
            {
                var agg = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var m in moves)
                {
                    var key = m.SrcId + ">" + m.TgtId + ">" + m.Norm;
                    agg[key] = (agg.TryGetValue(key, out var v) ? v : 0) + m.Amount;
                }
                var lines = new List<string>();
                foreach (var kv in agg) lines.Add(kv.Key + "=" + kv.Value);
                lines.Sort(StringComparer.Ordinal);
                return lines;
            };
            var p = normalize(OrganizePlanner.Plan(In(mk(false))).Moves);
            var q = normalize(OrganizePlanner.Plan(In(mk(true))).Moves);
            bool same = p.Count == q.Count;
            for (int i = 0; same && i < p.Count; i++) same = p[i] == q[i];
            Check(same, "permuting stack order / bucket first-seen order changes nothing in the plan");

            // (b) equal-demand tie: wood and stone both need 2 slots; the 2-slot chest 1 is nearer.
            //     Whoever is allocated first gets it. BucketRank must decide - in both directions.
            Func<List<ChestView>> tie = () => new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100, "wood"), S("stone", 100, "stone") },
                      totalSlots: 4, distance: 0.1f, excludedTarget: true),
                Chest(1, totalSlots: 2, distance: 1f),
                Chest(2, totalSlots: 24, distance: 2f),
            };
            var woodFirst = OrganizePlanner.Plan(In(tie(),
                rank: bkt => bkt == "wood" ? 0 : 1));
            Check(TotalTo(woodFirst.Moves, 1, "wood") == 100,
                "rank wood<stone: wood wins the near 2-slot chest");
            var stoneFirst = OrganizePlanner.Plan(In(tie(),
                rank: bkt => bkt == "stone" ? 0 : 1));
            Check(TotalTo(stoneFirst.Moves, 1, "stone") == 100,
                "rank stone<wood: stone wins it instead - a hash order cannot pass both");
        }

        // 31) KILLS M32 — adoption ignoring the anchored-bucket skip. An anchor is an instruction;
        //     incumbency is only a default. A bucket with a sign chest too small for it must spill
        //     into the NEARER free chest, not squat on the chest it happens to sit in — and the one
        //     psort_home of the run goes to the claimed spill chest, never the holder.
        Console.WriteLine("[31] adoption fills a vacuum only: an anchored bucket never self-adopts");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("iron", 100, "metals") }, totalSlots: 24, distance: 3f),
                Chest(1, totalSlots: 1, distance: 1f, anchors: Anchor("metals", AnchorKind.Sign)),
                Chest(2, totalSlots: 24, distance: 2f),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 1, "iron") == 50, "the 1-slot sign anchor fills first");
            Check(TotalTo(r.Moves, 2, "iron") == 50,
                "the spill claims the nearer free chest - adopting the holder would keep it there (M32)");
            Check(r.HomeMarks.Count == 1 && r.HomeMarks[0].ChestId == 2
                  && r.HomeMarks[0].BucketKey == "metals",
                "exactly one psort_home, on the claimed spill chest, never the anchored bucket's holder");
        }

        // 32) KILLS M17 / M02 — the reservedBy half of the shared key. A fresh phase-2 claim must
        //     fence its chest for the REST OF THIS RUN even though no anchor exists yet: the second
        //     bucket sees `shared` via reservedBy alone (both candidates rank non-empty, and the
        //     reserved chest is nearer, so only the shared key keeps the co-tenant out).
        Console.WriteLine("[32] a fresh claim fences its chest within the run (reservedBy)");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("iron", 200, "metals"), S("carrot", 100, "cooking") },
                      totalSlots: 24, distance: 0.1f, excludedTarget: true),
                Chest(1, totalSlots: 24, distance: 1f),
                Chest(2, new[] { S("junk", 10, null) }, totalSlots: 24, distance: 2f),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(TotalTo(r.Moves, 1, "iron") == 200, "the bigger bucket claims the nearer empty chest");
            Check(TotalTo(r.Moves, 2, "carrot") == 100 && TotalTo(r.Moves, 1, "carrot") == 0,
                "the second bucket respects the fresh claim and takes the farther chest (M17/M02 co-tenant)");

            // The deeper reason reservedBy must exist even though the marker fixed point can
            // untangle a squat after the fact: WITHOUT it, the untangling happens in the wrong
            // bucket's favour. Three buckets, ores(6 slots) > metals(4) = wood(4), three non-empty
            // 12-slot chests. The §16.4.1/[30] contract says the equal-demand tie resolves by rank
            // then ordinal — metals before wood — so metals gets the nearer chest. Drop reservedBy
            // and metals squats in ores' chest on the first pass, wood's spill marker claims the
            // middle chest meanwhile, and the settled base has metals and wood SWAPPED: same
            // convergence, inverted tie-break. (Found by a differential harness against exactly
            // this mutation; the fuzz alone cannot see it because both endpoints are stable.)
            var order = new List<ChestView>
            {
                Chest(0, new[] { S("copperore", 300, "ores"), S("iron", 200, "metals"),
                                 S("wood", 200, "wood") },
                      totalSlots: 48, distance: 0.1f, excludedTarget: true),
                Chest(1, new[] { S("junk1", 5, null) }, totalSlots: 12, distance: 1f),
                Chest(2, new[] { S("junk2", 5, null) }, totalSlots: 12, distance: 2f),
                Chest(3, new[] { S("junk3", 5, null) }, totalSlots: 12, distance: 3f),
            };
            var rOrder = OrganizePlanner.Plan(In(order));
            Check(TotalTo(rOrder.Moves, 1, "copperore") == 300, "largest demand takes the nearest chest");
            Check(TotalTo(rOrder.Moves, 2, "iron") == 200,
                "metals wins the equal-demand tie for the middle chest (ordinal, [30]'s contract)");
            Check(TotalTo(rOrder.Moves, 3, "wood") == 200,
                "and wood takes the far chest - dropping reservedBy settles them swapped (M17)");
        }

        // 33) The convergence fuzz, promoted from the scratchpad harness that caught the only two
        //     real regressions this week: 300 fixed-seed randomized bases per marker mode; press 2
        //     and press 3 must move zero items, homeless must never grow, and items must be
        //     conserved throughout. Three modes: stale markers, LIVE external markers (the shape
        //     the marker fixed point exists for), and incumbent-heavy bases (the adoption gate).
        Console.WriteLine("[33] convergence fuzz: 3x300 randomized bases settle by press 2");
        ConvergenceFuzz.Run(Check);

        // 34) The red-team's stale-marker adoption miss: an incumbent carrying a DELETED bucket's
        //     psort_home must still be adoptable. Adoption runs before the fixed point clears dead
        //     markers, so gating on "any marker at all" left the established ore chest unadoptable
        //     and geometry emptied it into a nearer empty chest - the exact complaint adoption
        //     exists to fix. Only a LIVE bucket's marker reserves a chest against adoption.
        Console.WriteLine("[34] a dead bucket's stale marker does not block adoption");
        {
            var chests = new List<ChestView>
            {
                Chest(0, totalSlots: 24, distance: 0.1f),
                Chest(1, totalSlots: 24, distance: 1f),
                Chest(2, new[] { S("copperore", 900, "ores") }, totalSlots: 24, distance: 5f,
                      anchors: Anchor("deadbucket", AnchorKind.Home), home: "deadbucket"),
            };
            var r = OrganizePlanner.Plan(In(chests));
            Check(r.Moves.Count == 0, "the ore stays in its established chest despite the stale marker");
            Check(r.HomeMarks.Count == 1 && r.HomeMarks[0].ChestId == 2
                  && r.HomeMarks[0].BucketKey == "ores",
                "one write: the stale marker is overwritten with the adopter's own (chest 2 -> ores)");
        }

        Console.WriteLine("================================");
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }
}
