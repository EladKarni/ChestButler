using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Registry of live containers (maintained by Container.Awake/OnDestroyed patches).</summary>
    internal static class ContainerTracker
    {
        private static readonly HashSet<Container> All = new HashSet<Container>();

        internal static void Register(Container c)
        {
            if (c != null) All.Add(c);
        }

        internal static void Unregister(Container c)
        {
            if (c == null) return;
            All.Remove(c);
            Filters.Invalidate(c);   // 1.1.2: the spec cache is keyed by Container and zone unload
                                     // mints a fresh one per chest, so stale keys accumulated forever
                                     // (each pinning a destroyed MonoBehaviour's object graph).
        }

        /// <summary>Valid target chests for a sorter, sorted by distance (closest first).
        /// 1.1.2: ties are broken by ZDO uid. The previous code enumerated a HashSet (zone-load
        /// order) and sorted with the UNSTABLE Array.Sort, so two chests at the same distance —
        /// a symmetric storage hall is the normal build — swapped ranks between sessions and the
        /// "stable tie-break" the Organize planner documents was not actually happening.</summary>
        internal static List<Container> Candidates(Container sorter, float radius, bool excludeSorters = true)
        {
            All.RemoveWhere(c => c == null);

            var pos = sorter.transform.position;
            var dists = new List<float>();
            var found = new List<Container>();

            foreach (var c in All)
            {
                if (c == sorter) continue;
                if (!SorterZdo.HasValidNView(c)) continue;
                if (c.GetInventory() == null) continue;
                if (c.GetComponentInParent<Piece>() == null) continue;   // player-built only
                float d = Vector3.Distance(pos, c.transform.position);
                if (d > radius) continue;
                if (excludeSorters && SorterZdo.IsSorter(c)) continue;  // sorters are never push targets
                if (!SorterZdo.PlayerCanAccess(c)) continue;             // container-level access
                if (!PrivateArea.CheckAccess(c.transform.position, 0f, false, true)) continue; // wards

                dists.Add(d);
                found.Add(c);
            }

            var order = new List<int>(found.Count);
            for (int i = 0; i < found.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int d = dists[a].CompareTo(dists[b]);
                if (d != 0) return d;
                return CompareUid(found[a], found[b]);      // deterministic across sessions
            });

            var sorted = new List<Container>(order.Count);
            foreach (var i in order) sorted.Add(found[i]);
            return sorted;
        }

        /// <summary>Stable, session-independent ordering for two live containers.</summary>
        internal static int CompareUid(Container a, Container b)
        {
            var na = SorterZdo.NView(a);
            var nb = SorterZdo.NView(b);
            if (na == null || !na.IsValid()) return (nb == null || !nb.IsValid()) ? 0 : 1;
            if (nb == null || !nb.IsValid()) return -1;
            return na.GetZDO().m_uid.CompareTo(nb.GetZDO().m_uid);
        }

        /// <summary>Nearest tracked container to a point, or null. Used to bind signs to one chest.</summary>
        internal static Container NearestTo(Vector3 point, float maxRange)
        {
            Container best = null;
            float bestD = maxRange;
            foreach (var c in All)
            {
                if (c == null) continue;
                float d = Vector3.Distance(point, c.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }
    }
}
