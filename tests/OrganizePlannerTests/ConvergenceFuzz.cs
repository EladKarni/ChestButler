using System;
using System.Collections.Generic;
using ChestButler.Core;

// Convergence fuzz, promoted into the suite from the scratchpad harness that caught the only two
// real regressions of the 2.0 cycle (the free-pool lockout and the one-item-per-press organize bug)
// before any hand-written section did.
//
// 300 FIXED seeds per mode. Each seed builds a randomized base covering everything the allocator
// claims to handle: station / sign / pin anchors, psort_home Home anchors, gear (unstackable
// one-slot items), per-type buckets that fold into misc, NG+ world-level stack variants (same
// bucket, unmergeable norm), immovable stacks, Sorter/Manual chests (drained, never filled) and
// sort:off chests (neither source nor target).
//
// THREE marker modes, because the marker is where convergence goes to die:
//   1. stale  — psort_home names a bucket that no longer exists; must be cleared and the chest
//               reused (§16.4.6 / §4.1).
//   2. live   — psort_home names a bucket that IS in the census, seeded onto a chest the planner
//               did not choose (same RNG stream as mode 1). This is the shape that used to shift
//               allocations for exactly one press — press 2 moved, press 3 settled — until Plan's
//               marker fixed point; the scope-out that once excluded it from this fuzz is
//               deliberately gone, and 300/300 here is the fix's acceptance test.
//   3. incumbent — every bucket already lives wholly in one unanchored, unmarked chest, with
//               emptier chests parked NEARER. Press 1 must move NOTHING: incumbent adoption (the
//               2.0.0 release gate) writes each bucket's psort_home onto the chest it already
//               occupies instead of emptying it into the nearest empty neighbour.
//
// For every seed: press 1, apply the plan (moves AND psort_home marks), then press 2 must move
// ZERO items, press 3 too, the homeless count must never grow, and per-norm item totals must be
// conserved throughout. Deterministic across processes: System.Random(seed) only, ordinal string
// comparisons everywhere, no clock, no reliance on string hash ordering.
internal static class ConvergenceFuzz
{
    private const int Seeds = 300;

    private static readonly string[] Groups =
    {
        "wood", "metals", "ores", "cooking", "meat", "seeds", "fuel", "hides", "stone", "ammo", "misc",
    };

    private static Dictionary<string, AnchorKind> A(AnchorKind k, params string[] buckets)
    {
        var d = new Dictionary<string, AnchorKind>(StringComparer.Ordinal);
        foreach (var b in buckets) d[b] = k;
        return d;
    }

    private static List<ChestView> Build(int seed, bool liveMarkers)
    {
        var rnd = new Random(seed);
        var chests = new List<ChestView>();
        int n = rnd.Next(3, 14);
        int weap = 0;

        for (int i = 0; i < n; i++)
        {
            var stacks = new List<StackView>();

            int k = rnd.Next(0, 5);
            for (int j = 0; j < k; j++)
            {
                var g = Groups[rnd.Next(Groups.Length)];
                string norm = g + "i" + rnd.Next(3);
                // NG+ world-level variant: same bucket, distinct merge identity -> its own slot.
                if (rnd.Next(6) == 0) norm += "L" + rnd.Next(2, 4);
                stacks.Add(new StackView { Norm = norm, Count = rnd.Next(1, 500), Stackable = true, BucketKey = g });
            }

            // gear: unstackable, one slot per item
            if (rnd.Next(4) == 0)
            {
                int w = rnd.Next(1, 9);
                for (int j = 0; j < w; j++)
                    stacks.Add(new StackView
                    {
                        Norm = "weap" + (weap++).ToString("000"),
                        Count = 1,
                        Stackable = false,
                        BucketKey = BucketKeys.Weapons,
                    });
            }

            // immovable stacks (adapter said: stays put, keeps its slot)
            if (rnd.Next(5) == 0)
                stacks.Add(new StackView { Norm = "locked" + i, Count = rnd.Next(1, 40), Stackable = true, BucketKey = null });

            // a small per-type bucket that must fold into misc (§16.4.1)
            if (rnd.Next(6) == 0)
                stacks.Add(new StackView
                {
                    Norm = "resin",
                    Count = rnd.Next(1, 30),
                    Stackable = true,
                    BucketKey = BucketKeys.ForType("resin"),
                });

            Dictionary<string, AnchorKind> anchors = null;
            string marker = null;
            int roll = rnd.Next(12);
            if (roll < 3) anchors = A(AnchorKind.Station, Groups[rnd.Next(Groups.Length)], Groups[rnd.Next(Groups.Length)]);
            else if (roll == 3) anchors = A(AnchorKind.Sign, Groups[rnd.Next(Groups.Length)]);
            else if (roll == 4) anchors = A(AnchorKind.Pin, Groups[rnd.Next(Groups.Length)]);
            else if (roll == 5)
            {
                // A psort_home from "last session", in the mode's flavour. Both flavours draw
                // Next(2), so the two sweeps are RNG-stream-identical and any divergence is the
                // marker's doing, not a different base.
                //
                //  - stale: a bucket that no longer exists. The planner must clear the marker and
                //    reuse the chest (§16.4.6 / §4.1).
                //  - live: a bucket with real demand, on a chest (often not wholly empty) that the
                //    planner would not pick fresh. Exactly the externally-seeded shape that used
                //    to destabilise press 2 before Plan iterated its markers to a fixed point.
                var g = liveMarkers ? Groups[rnd.Next(2)] : "dead" + rnd.Next(2);
                anchors = A(AnchorKind.Home, g);
                marker = g;
            }

            bool exclTarget = rnd.Next(12) == 0;              // Sorter / Manual: drained, never filled
            bool exclSource = exclTarget && rnd.Next(3) == 0; // sort: off — fully fenced

            chests.Add(new ChestView
            {
                Id = i,
                UidKey = "uid" + i.ToString("000"),
                Distance = i,
                TotalSlots = rnd.Next(1, 3) * 12,
                Priority = 0,
                Stacks = stacks,
                ExcludedAsTarget = exclTarget,
                ExcludedAsSource = exclSource,
                Anchors = anchors,
                HomeMarker = marker,
            });
        }
        return chests;
    }

    /// <summary>Mode 3: a settled, never-anchored base. Every bucket sits wholly inside its own
    /// chest (slot demand always fits), no anchors, no markers — and the nearest chests are all
    /// EMPTY, which is exactly the bait a fresh empty/distance pick takes and adoption must
    /// refuse. Returns the bucket count so the caller can assert one adoption marker each.</summary>
    private static List<ChestView> BuildIncumbent(int seed, out int bucketCount)
    {
        var rnd = new Random(seed);
        var chests = new List<ChestView>();
        int id = 0;

        int empties = rnd.Next(1, 4);
        for (int i = 0; i < empties; i++)
        {
            chests.Add(new ChestView
            {
                Id = id,
                UidKey = "uid" + id.ToString("000"),
                Distance = id,
                TotalSlots = 24,
                Priority = 0,
                Stacks = new List<StackView>(),
            });
            id++;
        }

        var pool = new List<string>(Groups);
        int k = rnd.Next(2, 7);
        bool withGear = rnd.Next(3) == 0;
        bucketCount = k + (withGear ? 1 : 0);

        for (int b = 0; b < k; b++)
        {
            var g = pool[rnd.Next(pool.Count)];
            pool.Remove(g);
            var stacks = new List<StackView>();
            int norms = rnd.Next(1, 4);           // <= 3 norms x <= 400 items = <= 24 slots: fits
            for (int j = 0; j < norms; j++)
                stacks.Add(new StackView { Norm = g + "i" + j, Count = rnd.Next(1, 401), Stackable = true, BucketKey = g });
            chests.Add(new ChestView
            {
                Id = id,
                UidKey = "uid" + id.ToString("000"),
                Distance = id,
                TotalSlots = 24,
                Priority = 0,
                Stacks = stacks,
            });
            id++;
        }

        if (withGear)
        {
            var stacks = new List<StackView>();
            int w = rnd.Next(1, 9);
            for (int j = 0; j < w; j++)
                stacks.Add(new StackView
                {
                    Norm = "weap" + j.ToString("000"),
                    Count = 1,
                    Stackable = false,
                    BucketKey = BucketKeys.Weapons,
                });
            chests.Add(new ChestView
            {
                Id = id,
                UidKey = "uid" + id.ToString("000"),
                Distance = id,
                TotalSlots = 24,
                Priority = 0,
                Stacks = stacks,
            });
            id++;
        }

        // occasional immovable-clutter chest at the far end; it owns no bucket, attracts nothing
        if (rnd.Next(3) == 0)
        {
            chests.Add(new ChestView
            {
                Id = id,
                UidKey = "uid" + id.ToString("000"),
                Distance = id,
                TotalSlots = 12,
                Priority = 0,
                Stacks = new List<StackView>
                {
                    new StackView { Norm = "locked" + id, Count = rnd.Next(1, 40), Stackable = true, BucketKey = null },
                },
            });
            id++;
        }

        return chests;
    }

    private static PlannerInput In(List<ChestView> chests)
        => new PlannerInput
        {
            Chests = chests,
            // gear norms are unstackable; everything else stacks to 50
            MaxStackOf = n => n.StartsWith("weap", StringComparison.Ordinal) ? 1 : 50,
            BucketRank = _ => 0,
            DistanceBetween = (a, b) => Math.Abs(chests[a].Distance - chests[b].Distance),
            MiscPromoteSlots = 24,
        };

    private static void Sweep(string label, Func<int, List<ChestView>> build, Action<bool, string> check)
    {
        int badPress2 = 0, badPress3 = 0, homelessGrew = 0, notConserved = 0;
        int firstBad = -1;

        for (int seed = 0; seed < Seeds; seed++)
        {
            var chests = build(seed);
            var before = Program.Totals(chests);

            var p1 = OrganizePlanner.Plan(In(chests));
            Program.Apply(chests, p1);
            var p2 = OrganizePlanner.Plan(In(chests));
            Program.Apply(chests, p2);
            var p3 = OrganizePlanner.Plan(In(chests));

            bool bad = false;
            if (p2.Moves.Count != 0) { badPress2++; bad = true; }
            if (p3.Moves.Count != 0) { badPress3++; bad = true; }
            if (p2.Summary.HomelessItems > p1.Summary.HomelessItems ||
                p3.Summary.HomelessItems > p2.Summary.HomelessItems) { homelessGrew++; bad = true; }
            if (!Program.SameTotals(before, Program.Totals(chests))) { notConserved++; bad = true; }
            if (bad && firstBad < 0) firstBad = seed;
        }

        if (firstBad >= 0)
            Console.WriteLine("        first bad seed (" + label + "): " + firstBad);

        check(badPress2 == 0, "press 2 moves ZERO items on all " + Seeds + " " + label + " bases (bad: " + badPress2 + ")");
        check(badPress3 == 0, "press 3 moves ZERO items on all " + Seeds + " " + label + " bases (bad: " + badPress3 + ")");
        check(homelessGrew == 0, label + ": the homeless count never grows after executing a plan (bad: " + homelessGrew + ")");
        check(notConserved == 0, label + ": per-norm item totals conserved across every press (bad: " + notConserved + ")");
    }

    internal static void Run(Action<bool, string> check)
    {
        Sweep("stale-marker", s => Build(s, false), check);
        Sweep("live-marker", s => Build(s, true), check);

        // ---- mode 3: incumbent-heavy bases -------------------------------------------------------
        int badP1 = 0, badMarks = 0, badP2 = 0, badP3 = 0, notCons = 0;
        int firstBadInc = -1;
        for (int seed = 0; seed < Seeds; seed++)
        {
            int k;
            var chests = BuildIncumbent(seed, out k);
            var before = Program.Totals(chests);

            var p1 = OrganizePlanner.Plan(In(chests));
            bool bad = false;
            if (p1.Moves.Count != 0) { badP1++; bad = true; }
            if (p1.HomeMarks.Count != k) { badMarks++; bad = true; }
            Program.Apply(chests, p1);
            var p2 = OrganizePlanner.Plan(In(chests));
            Program.Apply(chests, p2);
            var p3 = OrganizePlanner.Plan(In(chests));
            if (p2.Moves.Count != 0) { badP2++; bad = true; }
            if (p3.Moves.Count != 0) { badP3++; bad = true; }
            if (!Program.SameTotals(before, Program.Totals(chests))) { notCons++; bad = true; }
            if (bad && firstBadInc < 0) firstBadInc = seed;
        }
        if (firstBadInc >= 0)
            Console.WriteLine("        first bad seed (incumbent): " + firstBadInc);

        check(badP1 == 0, "incumbent bases: press 1 moves NOTHING - adoption keeps every settled bucket (bad: " + badP1 + ")");
        check(badMarks == 0, "incumbent bases: exactly one adoption psort_home per bucket, on its own chest (bad: " + badMarks + ")");
        check(badP2 == 0, "press 2 moves ZERO items on all " + Seeds + " incumbent bases (bad: " + badP2 + ")");
        check(badP3 == 0, "press 3 moves ZERO items on all " + Seeds + " incumbent bases (bad: " + badP3 + ")");
        check(notCons == 0, "incumbent: per-norm item totals conserved across every press (bad: " + notCons + ")");

        // ---- the owner's ore chest, deterministically --------------------------------------------
        // 900 ore in a chest nobody ever pinned, with TWO empty chests nearer. The pre-adoption
        // planner emptied the ore into chest 0 (nearest wholly-empty wins); the release gate says
        // zero ore moves and the psort_home lands on the ore chest itself.
        var ore = new List<ChestView>
        {
            new ChestView { Id = 0, UidKey = "uid000", Distance = 0, TotalSlots = 24, Stacks = new List<StackView>() },
            new ChestView { Id = 1, UidKey = "uid001", Distance = 1, TotalSlots = 24, Stacks = new List<StackView>() },
            new ChestView
            {
                Id = 2, UidKey = "uid002", Distance = 2, TotalSlots = 24,
                Stacks = new List<StackView>
                {
                    new StackView { Norm = "copperore", Count = 900, Stackable = true, BucketKey = "ores" },
                },
            },
        };
        var op1 = OrganizePlanner.Plan(In(ore));
        check(op1.Moves.Count == 0, "900 unanchored ore: ZERO moves - the established chest is adopted, not emptied");
        check(op1.HomeMarks.Count == 1 && op1.HomeMarks[0].ChestId == 2 && op1.HomeMarks[0].BucketKey == "ores",
            "and the psort_home is written on the ore chest itself");
        Program.Apply(ore, op1);
        var op2 = OrganizePlanner.Plan(In(ore));
        check(op2.Moves.Count == 0, "the adopted ore chest is stable on press 2");
    }
}
