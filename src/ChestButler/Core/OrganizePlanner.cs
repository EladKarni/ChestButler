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
        public Func<string, bool> PinsItem;        // explicit item pin/sign token matches (Router tier 3)
        public Func<string, bool> PinsGroup;       // sign group matches (Router tier 2)
        public Func<string, bool> StationAttracts; // adjacent crafting station attracts this norm
        public int Priority;                       // sign priority (pN); ranks within a tier like Router
        public int EmptySlots;                     // free slots — a SHARED resource across item types
        public Func<string, int> PartialSpaceFor;  // free space in existing partial stacks of this norm
        public Func<string, int> MaxStackOf;       // max stack size of this norm (1 = non-stackable)

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

    /// <summary>PURE, deterministic planner. For each item type it chooses ONE winning destination,
    /// mirroring Router's ranking exactly so Organize and the live sorter never disagree (no
    /// ping-pong): explicit item pin &gt; group/sign match &gt; station adjacency &gt; most-held;
    /// within a tier, sign priority (pN) &gt; most-held &gt; nearest (input order). Capacity is
    /// slot-accurate: empty slots are a shared resource across all item types routed to the same
    /// chest, so the preview never promises more than actually fits. Non-stackables (tools/armor)
    /// are skipped unless some target pins them. No Unity, no config, no randomness.</summary>
    internal static class OrganizePlanner
    {
        private struct Holder { public int ChestId; public int StackIndex; public int Count; }
        private enum Tier { PinItem, PinGroup, Station, Holds }

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

            // Empty slots remaining per target — shared across every item type routed there.
            var emptyLeft = new Dictionary<int, int>();
            var srcSet = new HashSet<int>();
            var tgtSet = new HashSet<int>();

            foreach (var norm in order)
            {
                int targetId = ChooseTarget(chests, norm);
                if (targetId < 0) continue;                       // nothing wants it → leave in place

                var target = chests[targetId];
                if (!emptyLeft.ContainsKey(targetId)) emptyLeft[targetId] = Math.Max(0, target.EmptySlots);

                int maxStack = Math.Max(1, target.MaxStackOf != null ? target.MaxStackOf(norm) : 1);
                int partialLeft = Math.Max(0, target.PartialSpaceFor != null ? target.PartialSpaceFor(norm) : 0);
                int slotFill = 0;   // space left in the newly opened slot of THIS norm

                foreach (var h in byType[norm])
                {
                    if (h.ChestId == targetId) continue;          // never move a chest into itself
                    int avail = partialLeft + slotFill + emptyLeft[targetId] * maxStack;
                    int amount = Math.Min(h.Count, avail);
                    if (amount <= 0) break;                       // target full → overflow stays in source

                    // consume capacity: partial stacks first, then the open slot, then fresh slots
                    int rest = amount;
                    int fromPartial = Math.Min(rest, partialLeft);
                    partialLeft -= fromPartial; rest -= fromPartial;
                    int fromOpen = Math.Min(rest, slotFill);
                    slotFill -= fromOpen; rest -= fromOpen;
                    while (rest > 0)
                    {
                        emptyLeft[targetId]--;                    // open a fresh slot (shared resource)
                        slotFill = maxStack;
                        int take = Math.Min(rest, slotFill);
                        slotFill -= take; rest -= take;
                    }

                    moves.Add(new OrganizeMove
                    {
                        SrcId = h.ChestId,
                        SrcStackIndex = h.StackIndex,
                        TgtId = targetId,
                        Norm = norm,
                        Amount = amount
                    });
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
                if (c.PinsItem != null && c.PinsItem(norm)) return true;
                if (c.PinsGroup != null && c.PinsGroup(norm)) return true;
            }
            return false;
        }

        private static int ChooseTarget(IReadOnlyList<ChestView> chests, string norm)
        {
            int pick = PickBest(chests, norm, Tier.PinItem);
            if (pick >= 0) return pick;
            pick = PickBest(chests, norm, Tier.PinGroup);
            if (pick >= 0) return pick;
            pick = PickBest(chests, norm, Tier.Station);
            if (pick >= 0) return pick;
            return PickBest(chests, norm, Tier.Holds);
        }

        /// <summary>Within the chests qualifying for <paramref name="tier"/>, rank like Router:
        /// sign priority, then most-held, then earliest (nearest). -1 if none qualify.</summary>
        private static int PickBest(IReadOnlyList<ChestView> chests, string norm, Tier tier)
        {
            int bestId = -1, bestPrio = int.MinValue, bestHeld = -1;
            for (int i = 0; i < chests.Count; i++)     // input order == nearest-first
            {
                var c = chests[i];
                if (c == null || c.ExcludedAsTarget) continue;

                bool qualifies;
                switch (tier)
                {
                    case Tier.PinItem:  qualifies = c.PinsItem != null && c.PinsItem(norm); break;
                    case Tier.PinGroup: qualifies = c.PinsGroup != null && c.PinsGroup(norm); break;
                    case Tier.Station:  qualifies = c.StationAttracts != null && c.StationAttracts(norm); break;
                    default:            qualifies = c.HeldOf(norm) > 0; break;
                }
                if (!qualifies) continue;

                int held = c.HeldOf(norm);
                if (c.Priority > bestPrio ||
                    (c.Priority == bestPrio && held > bestHeld))  // strictly greater → nearest wins ties
                {
                    bestPrio = c.Priority;
                    bestHeld = held;
                    bestId = i;
                }
            }
            return bestId;
        }
    }
}
