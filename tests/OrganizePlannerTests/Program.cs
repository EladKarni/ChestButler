using System;
using System.Collections.Generic;
using ChestButler.Core;

// Hand-rolled, dependency-free test runner for the pure OrganizePlanner (plan §8).
// Chests are addressed by their position in the input list (index == chest id in the plan output),
// which the planner also treats as nearest-first ordering.
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(bool cond, string msg)
    {
        if (cond) { _passed++; Console.WriteLine("  PASS  " + msg); }
        else { _failed++; Console.WriteLine("  FAIL  " + msg); }
    }

    // Build a ChestView. Affinity/capacity are delegates (that is what keeps the planner Unity-free).
    private static ChestView Chest(int id, (string norm, int count, bool stackable)[] stacks = null,
        string[] pins = null, string[] station = null, int room = 1000, bool excluded = false)
    {
        var sv = new List<StackView>();
        if (stacks != null)
            foreach (var s in stacks)
                sv.Add(new StackView { Norm = s.norm, Count = s.count, Stackable = s.stackable });

        var pinSet = new HashSet<string>(pins ?? new string[0]);
        var stSet = new HashSet<string>(station ?? new string[0]);

        return new ChestView
        {
            Id = id,
            ExcludedAsTarget = excluded,
            Stacks = sv,
            Pins = n => pinSet.Contains(n),
            StationAttracts = n => stSet.Contains(n),
            RoomFor = n => room
        };
    }

    private static (string norm, int count, bool stackable) S(string norm, int count, bool stackable = true)
        => (norm, count, stackable);

    private static int MovesTo(List<OrganizeMove> moves, int tgt)
    {
        int n = 0;
        foreach (var m in moves) if (m.TgtId == tgt) n++;
        return n;
    }

    private static int TotalTo(List<OrganizeMove> moves, int tgt)
    {
        int n = 0;
        foreach (var m in moves) if (m.TgtId == tgt) n += m.Amount;
        return n;
    }

    private static bool AnySelfMove(List<OrganizeMove> moves)
    {
        foreach (var m in moves) if (m.SrcId == m.TgtId) return true;
        return false;
    }

    private static int Main()
    {
        Console.WriteLine("OrganizePlanner unit tests");
        Console.WriteLine("==========================");

        // 1) Empty input -> empty plan.
        Console.WriteLine("[1] empty input -> empty plan");
        {
            var moves = OrganizePlanner.Plan(new List<ChestView>(), out var sum);
            Check(moves.Count == 0, "no moves");
            Check(sum.TotalItems == 0 && sum.TargetChests == 0 && sum.SourceChests == 0, "zero summary");
        }

        // 2) Consolidation: most-held wins; no self-move.
        Console.WriteLine("[2] most-held wins consolidation, no self-move");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 5) }),
                Chest(1, new[] { S("wood", 10) }),
            };
            var moves = OrganizePlanner.Plan(chests, out var sum);
            Check(moves.Count == 1, "exactly one move");
            Check(moves[0].SrcId == 0 && moves[0].TgtId == 1 && moves[0].Amount == 5, "wood 0->1 x5 (into fullest)");
            Check(!AnySelfMove(moves), "no self-move");
            Check(sum.TotalItems == 5 && sum.TargetChests == 1 && sum.SourceChests == 1, "summary 5/1/1");
        }

        // 3) Tie on most-held -> nearest (lower index) wins.
        Console.WriteLine("[3] most-held tie -> nearest wins");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 5) }),
                Chest(1, new[] { S("wood", 5) }),
            };
            var moves = OrganizePlanner.Plan(chests, out _);
            Check(moves.Count == 1 && moves[0].TgtId == 0 && moves[0].SrcId == 1, "tie routes 1->0 (nearest)");
        }

        // 4) Priority: pin beats station beats most-held.
        Console.WriteLine("[4] pin > station > most-held");
        {
            // pin present -> pin wins over station (chest0) and most-held (chest2)
            var pinCase = new List<ChestView>
            {
                Chest(0, new[] { S("iron", 1) }, station: new[] { "iron" }),
                Chest(1, pins: new[] { "iron" }),
                Chest(2, new[] { S("iron", 50) }),
                Chest(3, new[] { S("iron", 3) }),
            };
            var m1 = OrganizePlanner.Plan(pinCase, out var s1);
            Check(MovesTo(m1, 1) == m1.Count && m1.Count > 0, "all iron routes to the pinning chest (1)");
            Check(TotalTo(m1, 1) == 54, "54 iron consolidated into pin (1+50+3)");

            // no pin -> station beats most-held
            var stationCase = new List<ChestView>
            {
                Chest(0, new[] { S("iron", 1) }, station: new[] { "iron" }),
                Chest(2, new[] { S("iron", 50) }),
                Chest(3, new[] { S("iron", 3) }),
            };
            var m2 = OrganizePlanner.Plan(stationCase, out _);
            Check(MovesTo(m2, 0) == m2.Count && m2.Count > 0, "station chest (0) wins despite holding least");
            Check(!AnySelfMove(m2), "station target does not move into itself");
        }

        // 5) Non-stackables (tools/armor) stay put unless a chest pins them.
        Console.WriteLine("[5] non-stackables excluded unless pinned");
        {
            var noPin = new List<ChestView>
            {
                Chest(0, new[] { S("bronzesword", 1, false) }),
                Chest(1, new[] { S("bronzesword", 1, false) }),
            };
            var m1 = OrganizePlanner.Plan(noPin, out _);
            Check(m1.Count == 0, "un-pinned gear stays put");

            var pinned = new List<ChestView>
            {
                Chest(0, new[] { S("bronzesword", 1, false) }),
                Chest(1, new[] { S("bronzesword", 1, false) }),
                Chest(2, pins: new[] { "bronzesword" }, room: 1), // Router.Room yields 1 for non-stackables
            };
            var m2 = OrganizePlanner.Plan(pinned, out _);
            Check(m2.Count == 1 && m2[0].TgtId == 2, "one gear piece moves to the pinning chest (room=1)");
        }

        // 6) Capacity respected; overflow stays in source.
        Console.WriteLine("[6] capacity + overflow");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 40) }),
                Chest(1, new[] { S("wood", 40) }),
                Chest(2, station: new[] { "wood" }, room: 50), // home has room for only 50
            };
            var moves = OrganizePlanner.Plan(chests, out var sum);
            Check(TotalTo(moves, 2) == 50, "exactly 50 wood delivered (capped by room)");
            Check(sum.TotalItems == 50, "summary counts only delivered items");
            int fromEach = 0; foreach (var m in moves) fromEach += m.Amount;
            Check(fromEach == 50, "30 wood overflow left behind in source(s)");
        }

        // 7) Ignore / ManualOnly / Sorter chests are never targets (but can be sources).
        Console.WriteLine("[7] excluded chests are never targets, still sources");
        {
            // excluded chest holds the most AND pins the item, yet must not be chosen
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100) }, pins: new[] { "wood" }, excluded: true), // e.g. sorter/Ignore/ManualOnly
                Chest(1, new[] { S("wood", 1) }),
            };
            var moves = OrganizePlanner.Plan(chests, out _);
            Check(moves.Count == 1 && moves[0].TgtId == 1 && moves[0].SrcId == 0,
                "wood flows OUT of the excluded chest (0) into the normal chest (1)");
            Check(TotalTo(moves, 0) == 0, "nothing is routed INTO the excluded chest");
        }

        Console.WriteLine("==========================");
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }
}
