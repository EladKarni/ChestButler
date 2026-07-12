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
            if (c != null) All.Remove(c);
        }

        /// <summary>Valid target chests for a sorter, sorted by distance (closest first).</summary>
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

            var keys = dists.ToArray();
            var items = found.ToArray();
            Array.Sort(keys, items);
            return new List<Container>(items);
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
