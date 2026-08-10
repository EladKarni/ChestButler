using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Registry of live containers (maintained by Container.Awake/OnDestroyed patches).</summary>
    internal static class ContainerTracker
    {
        private static readonly HashSet<Container> All = new HashSet<Container>();

        /// <summary>Containers that belong to a VEHICLE — a cart bed (Vagon) or a ship's hold. They
        /// are transport, not storage: the staging soak had Organize filing loot into the owner's
        /// wagons. Classified once at registration (the parent component never changes) and skipped
        /// by the accessibility core in BOTH directions unless [Sorting] VehiclesAreStorage is on,
        /// so the tick, Organize, Pull-sources and Gather all agree. Their own chest UI still works:
        /// pin a cart and press Pull to LOAD it for a trip, Take All to unload — manual stays manual.</summary>
        private static readonly HashSet<Container> Vehicles = new HashSet<Container>();

        internal static void Register(Container c)
        {
            if (c == null) return;

            // The Obliterator's "chest" is a destruction chute — its Container + Piece pass every
            // generic filter, and routing so much as one stack into it would delete items on the
            // next lever pull. Never registered, no config, no exceptions.
            if (c.GetComponentInParent<Incinerator>() != null) return;

            All.Add(c);
            if (c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null)
                Vehicles.Add(c);
        }

        internal static void Unregister(Container c)
        {
            if (c == null) return;
            All.Remove(c);
            Vehicles.Remove(c);
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
            => Accessible(sorter.transform.position, radius, sorter, excludeSorters);

        /// <summary>W2 (Gather): the same accessibility query centred on an arbitrary POINT rather than
        /// on a chest.
        ///
        /// Every existing query measures from a Container and excludes that Container from its own
        /// results, which is right for a sorter pushing items outward but wrong for Gather, whose
        /// origin is the player. Passing the nearest chest as a stand-in would centre the radius on the
        /// wrong point and then drop that chest from the results.
        ///
        /// Deliberately a thin call into the same private core as <see cref="Candidates"/> rather than
        /// a second copy of the filter chain: that chain is on the sorter tick path and is load-bearing
        /// for Organize, and two copies drifting apart about what "accessible" means is a worse outcome
        /// than one shared implementation.</summary>
        internal static List<Container> AccessibleNear(Vector3 point, float radius, bool excludeSorters = false)
            => Accessible(point, radius, null, excludeSorters);

        private static List<Container> Accessible(Vector3 pos, float radius, Container exclude, bool excludeSorters)
        {
            All.RemoveWhere(c => c == null);

            var dists = new List<float>();
            var found = new List<Container>();

            bool vehiclesAreStorage = Plugin.VehiclesAreStorage != null && Plugin.VehiclesAreStorage.Value;

            foreach (var c in All)
            {
                if (c == exclude) continue;
                if (!vehiclesAreStorage && Vehicles.Contains(c)) continue; // transport, not storage
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

        /// <summary>A candidate chest together with its distance from the query point.</summary>
        internal struct Candidate
        {
            internal Container Chest;
            internal float Distance;
        }

        /// <summary>WAVE 0 — same query as <see cref="Candidates"/>, but keeps the distances.
        ///
        /// Organize v2 needs them: it places a bucket's overflow "nearest-first within the bucket"
        /// and picks new homes by distance to a station or to the origin sorter, and the plain
        /// Candidates call throws that information away. Ordering is identical (distance, then ZDO
        /// uid), so the two never disagree about which chest is nearer.</summary>
        internal static List<Candidate> CandidatesWithDistance(Container sorter, float radius, bool excludeSorters = true)
        {
            var chests = Candidates(sorter, radius, excludeSorters);
            var result = new List<Candidate>(chests.Count);
            var pos = sorter.transform.position;
            foreach (var c in chests)
                result.Add(new Candidate { Chest = c, Distance = Vector3.Distance(pos, c.transform.position) });
            return result;
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
