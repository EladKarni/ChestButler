using System;
using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>One movable stack seen inside a chest (normalized type + count + stackability).</summary>
    internal struct StackView
    {
        public string Norm;
        public int Count;
        public bool Stackable;   // m_maxStackSize > 1
    }

    /// <summary>A chest as the planner sees it. Deliberately Unity-free: affinity and capacity are
    /// supplied as delegates so the planner has no dependency on Groups/Filters/Router/Unity and can
    /// be unit-tested offline. The live Unity adapter (<see cref="Organizer"/>) fills these in.</summary>
    internal sealed class ChestView
    {
        public int Id;
        public bool ExcludedAsTarget;              // Sorter, FilterSpec.Ignore or ManualOnly → never a destination
        public List<StackView> Stacks;             // current contents (movable stacks only)
        public Func<string, bool> Pins;            // filter (pin/sign) matches this norm
        public Func<string, bool> StationAttracts; // adjacent crafting station attracts this norm
        public Func<string, int> RoomFor;          // capacity, in item units, this chest has for this norm (>= 0)

        public int HeldOf(string norm)
        {
            int n = 0;
            if (Stacks != null)
                for (int i = 0; i < Stacks.Count; i++)
                    if (Stacks[i].Norm == norm) n += Stacks[i].Count;
            return n;
        }
    }

    /// <summary>One planned transfer: move <c>Amount</c> of <c>Norm</c> from source chest
    /// <c>SrcId</c> (stack <c>SrcStackIndex</c>) into target chest <c>TgtId</c>.</summary>
    internal struct OrganizeMove
    {
        public int SrcId;
        public int SrcStackIndex;
        public int TgtId;
        public string Norm;
        public int Amount;
    }

    internal struct OrganizeSummary
    {
        public int TotalItems;    // sum of moved amounts
        public int TargetChests;  // distinct destinations
        public int SourceChests;  // distinct sources
    }

    /// <summary>PURE, deterministic planner (plan §3). For each item type it chooses ONE winning
    /// destination — pin (tier 4) &gt; station adjacency (tier 3) &gt; most-held consolidation
    /// (tier 2); within a tier the most-held chest wins, ties broken by nearest (input order) — then
    /// moves every other instance into it, capped by the target's room. No self-moves, no ping-pong:
    /// each type is decided once. Non-stackables (tools/armor) are skipped unless some target pins
    /// them. No Unity, no config, no randomness.</summary>
    internal static class OrganizePlanner
    {
        private struct Holder { public int ChestId; public int StackIndex; public int Count; }
        private enum Tier { Pin, Station, Holds }

        internal static List<OrganizeMove> Plan(IReadOnlyList<ChestView> chests, out OrganizeSummary summary)
        {
            var moves = new List<OrganizeMove>();
            summary = default(OrganizeSummary);
            if (chests == null || chests.Count == 0) return moves;

            // Group every movable stack by normalized type. `order` keeps type iteration deterministic
            // (first-seen), and chests are already nearest-first so holders inherit that order.
            var byType = new Dictionary<string, List<Holder>>();
            var order = new List<string>();
            for (int ci = 0; ci < chests.Count; ci++)
            {
                var c = chests[ci];
                if (c == null || c.Stacks == null) continue;
                for (int si = 0; si < c.Stacks.Count; si++)
                {
                    var s = c.Stacks[si];
                    if (string.IsNullOrEmpty(s.Norm) || s.Count <= 0) continue;
                    if (!s.Stackable && !AnyTargetPins(chests, s.Norm)) continue; // gear stays put unless pinned
                    if (!byType.TryGetValue(s.Norm, out var list))
                    {
                        list = new List<Holder>();
                        byType[s.Norm] = list;
                        order.Add(s.Norm);
                    }
                    list.Add(new Holder { ChestId = ci, StackIndex = si, Count = s.Count });
                }
            }

            var srcSet = new HashSet<int>();
            var tgtSet = new HashSet<int>();

            foreach (var norm in order)
            {
                int targetId = ChooseTarget(chests, norm);
                if (targetId < 0) continue;                       // nothing wants it → leave in place

                int room = chests[targetId].RoomFor != null ? chests[targetId].RoomFor(norm) : 0;
                if (room <= 0) continue;                          // home is full → overflow stays in source

                foreach (var h in byType[norm])
                {
                    if (h.ChestId == targetId) continue;          // never move a chest into itself
                    int amount = Math.Min(h.Count, room);
                    if (amount <= 0) break;                       // target full
                    moves.Add(new OrganizeMove
                    {
                        SrcId = h.ChestId,
                        SrcStackIndex = h.StackIndex,
                        TgtId = targetId,
                        Norm = norm,
                        Amount = amount
                    });
                    room -= amount;
                    summary.TotalItems += amount;
                    srcSet.Add(h.ChestId);
                    tgtSet.Add(targetId);
                }
            }

            summary.SourceChests = srcSet.Count;
            summary.TargetChests = tgtSet.Count;
            return moves;
        }

        private static bool AnyTargetPins(IReadOnlyList<ChestView> chests, string norm)
        {
            for (int i = 0; i < chests.Count; i++)
            {
                var c = chests[i];
                if (c == null || c.ExcludedAsTarget) continue;
                if (c.Pins != null && c.Pins(norm)) return true;
            }
            return false;
        }

        private static int ChooseTarget(IReadOnlyList<ChestView> chests, string norm)
        {
            int pick = PickMostHeld(chests, norm, Tier.Pin);
            if (pick >= 0) return pick;
            pick = PickMostHeld(chests, norm, Tier.Station);
            if (pick >= 0) return pick;
            return PickMostHeld(chests, norm, Tier.Holds);
        }

        /// <summary>Within the chests qualifying for <paramref name="tier"/>, return the one holding
        /// the most of <paramref name="norm"/>; ties go to the earliest (nearest) chest. -1 if none.</summary>
        private static int PickMostHeld(IReadOnlyList<ChestView> chests, string norm, Tier tier)
        {
            int bestId = -1, bestHeld = -1;
            for (int i = 0; i < chests.Count; i++)     // input order == nearest-first
            {
                var c = chests[i];
                if (c == null || c.ExcludedAsTarget) continue;

                bool qualifies;
                switch (tier)
                {
                    case Tier.Pin:     qualifies = c.Pins != null && c.Pins(norm); break;
                    case Tier.Station: qualifies = c.StationAttracts != null && c.StationAttracts(norm); break;
                    default:           qualifies = c.HeldOf(norm) > 0; break;
                }
                if (!qualifies) continue;

                int held = c.HeldOf(norm);
                if (held > bestHeld) { bestHeld = held; bestId = i; }   // strictly greater → nearest wins ties
            }
            return bestId;
        }
    }
}
