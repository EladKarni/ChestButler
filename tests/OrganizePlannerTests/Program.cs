using System;
using System.Collections.Generic;
using ChestButler.Core;

// Hand-rolled, dependency-free test runner for the pure OrganizePlanner.
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
        string[] pins = null, string[] groupPins = null, string[] station = null,
        int priority = 0, int emptySlots = 100, int maxStack = 50,
        Dictionary<string, int> partial = null, bool excluded = false)
    {
        var sv = new List<StackView>();
        if (stacks != null)
            foreach (var s in stacks)
                sv.Add(new StackView { Norm = s.norm, Count = s.count, Stackable = s.stackable });

        var pinSet = new HashSet<string>(pins ?? new string[0]);
        var grpSet = new HashSet<string>(groupPins ?? new string[0]);
        var stSet = new HashSet<string>(station ?? new string[0]);

        return new ChestView
        {
            Id = id,
            ExcludedAsTarget = excluded,
            Stacks = sv,
            PinsItem = n => pinSet.Contains(n),
            PinsGroup = n => grpSet.Contains(n),
            StationAttracts = n => stSet.Contains(n),
            Priority = priority,
            EmptySlots = emptySlots,
            PartialSpaceFor = n => partial != null && partial.TryGetValue(n, out var v) ? v : 0,
            MaxStackOf = n => maxStack
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

    private static int TotalTo(List<OrganizeMove> moves, int tgt, string norm = null)
    {
        int n = 0;
        foreach (var m in moves) if (m.TgtId == tgt && (norm == null || m.Norm == norm)) n += m.Amount;
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

        // 4) Tier order: item pin > station > most-held.
        Console.WriteLine("[4] pin > station > most-held");
        {
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

        // 5) Non-stackables (tools/armor) stay put unless pinned; each consumes one empty slot.
        Console.WriteLine("[5] non-stackables excluded unless pinned; slot-limited");
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
                Chest(2, pins: new[] { "bronzesword" }, emptySlots: 1, maxStack: 1),
            };
            var m2 = OrganizePlanner.Plan(pinned, out _);
            Check(m2.Count == 1 && m2[0].TgtId == 2, "only one sword fits the single empty slot");
        }

        // 6) Capacity respected; overflow stays in source.
        Console.WriteLine("[6] capacity + overflow");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 40) }),
                Chest(1, new[] { S("wood", 40) }),
                Chest(2, station: new[] { "wood" }, emptySlots: 0,
                      partial: new Dictionary<string, int> { { "wood", 50 } }),
            };
            var moves = OrganizePlanner.Plan(chests, out var sum);
            Check(TotalTo(moves, 2) == 50, "exactly 50 wood delivered (capped by partial space)");
            Check(sum.TotalItems == 50, "summary counts only delivered items");
        }

        // 7) Ignore / ManualOnly / Sorter chests are never targets (but can be sources).
        Console.WriteLine("[7] excluded chests are never targets, still sources");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100) }, pins: new[] { "wood" }, excluded: true),
                Chest(1, new[] { S("wood", 1) }),
            };
            var moves = OrganizePlanner.Plan(chests, out _);
            Check(moves.Count == 1 && moves[0].TgtId == 1 && moves[0].SrcId == 0,
                "wood flows OUT of the excluded chest (0) into the normal chest (1)");
            Check(TotalTo(moves, 0) == 0, "nothing is routed INTO the excluded chest");
        }

        // 8) Empty slots are SHARED across item types routed to one chest (no overcommit).
        Console.WriteLine("[8] shared empty slots: preview cannot overcommit a target");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 100) }),
                Chest(1, new[] { S("stone", 100) }),
                Chest(2, pins: new[] { "wood", "stone" }, emptySlots: 1, maxStack: 50,
                      partial: new Dictionary<string, int> { { "wood", 40 }, { "stone", 40 } }),
            };
            var moves = OrganizePlanner.Plan(chests, out var sum);
            Check(TotalTo(moves, 2, "wood") == 90, "wood gets its partial 40 + the single slot (50) = 90");
            Check(TotalTo(moves, 2, "stone") == 40, "stone gets only its partial 40 (slot already spent)");
            Check(sum.TotalItems == 130, "summary promises 130, not the naive 180");
        }

        // 9) Router parity: explicit item pin beats group/sign match even when the group chest holds more.
        Console.WriteLine("[9] item pin beats group match (no sorter ping-pong)");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("finewood", 20) }),
                Chest(1, new[] { S("finewood", 50) }, groupPins: new[] { "finewood" }),
                Chest(2, new[] { S("finewood", 10) }, pins: new[] { "finewood" }),
            };
            var moves = OrganizePlanner.Plan(chests, out _);
            Check(MovesTo(moves, 2) == moves.Count && moves.Count > 0, "all finewood routes to the item-pin chest (2)");
            Check(TotalTo(moves, 2) == 70, "group chest is drained into the pin chest (20+50), matching Router");
        }

        // 10) Sign priority (pN) ranks within a tier before most-held, like Router.
        Console.WriteLine("[10] priority beats most-held within a tier");
        {
            var chests = new List<ChestView>
            {
                Chest(0, new[] { S("wood", 30) }),
                Chest(1, new[] { S("wood", 100) }, groupPins: new[] { "wood" }, priority: 0),
                Chest(2, new[] { S("wood", 1) }, groupPins: new[] { "wood" }, priority: 5),
            };
            var moves = OrganizePlanner.Plan(chests, out _);
            Check(MovesTo(moves, 2) == moves.Count && moves.Count > 0, "p5 chest wins despite holding least");
            Check(TotalTo(moves, 2) == 130, "both other chests drain into the p5 chest (30+100)");
        }

        Console.WriteLine("==========================");
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }
}
