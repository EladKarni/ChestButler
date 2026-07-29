using System.Collections.Generic;
using UnityEngine;

namespace ChestButler.Core
{
    internal sealed class FilterSpec
    {
        internal readonly HashSet<string> Items = new HashSet<string>();   // exact/wildcard tokens
        internal readonly HashSet<string> GroupNames = new HashSet<string>();
        internal int Priority;
        internal bool Ignore;
        internal bool ManualOnly;   // pull-only buffer: sorter never auto-fills it

        /// <summary>The bucket this chest was claimed as a home for by a previous Organize v2 run
        /// (the <c>psort_home</c> ZDO key), or null. Cached alongside the rest of the spec so a plan
        /// costs one ZDO read per chest rather than two. v2 plan §4.1.</summary>
        internal string Home;

        internal bool HasExplicit => Items.Count > 0 || GroupNames.Count > 0;

        internal bool MatchesItem(string normName)
        {
            foreach (var t in Items)
                if (Names.Matches(t, normName)) return true;
            return false;
        }

        internal bool MatchesGroup(string normName)
        {
            foreach (var g in GroupNames)
                if (Groups.GroupContains(g, normName)) return true;
            return false;
        }
    }

    /// <summary>Per-chest filters from two sources:
    /// 1. Pinned items - "Pin contents" button, stored as CSV in the chest ZDO.
    /// 2. Sign labels - a sign within range reading "sort: cooking, stone, p2".
    /// Tokens: group names, item tokens ('*' wildcards), pN = priority, off = ignore chest.</summary>
    internal static class Filters
    {
        private const float SignRange = 2.5f;

        // 1.1.2: was 3 s, i.e. shorter than nothing that actually invalidates it and close enough to
        // the sorter tick that the cache missed constantly. Every miss re-sweeps every registered
        // Sign and runs ContainerTracker.NearestTo (a full container scan) per in-range sign, which
        // is O(chests^2) on the tick path. The cache is now invalidated explicitly instead: on pin
        // or manual-flag change (SetPinned/SetManual), on sign text change and on chest unload.
        private const float CacheTtl = 30f;
        private static readonly int ItemsHash = "psort_items".GetStableHashCode();
        private static readonly int ManualHash = "psort_manual".GetStableHashCode();

        /// <summary>W1 (Organize v2, plan §4.1/§16.1): the bucket key a chest was claimed as a home
        /// for. Without this the allocator has no fixed point — §4 step 4 asks for an EMPTY chest to
        /// claim, run 1 makes its own claimed chests non-empty, so run 2 claims a different empty
        /// chest and relocates the whole spill. A bucket with no other anchor moves 100% of itself on
        /// every press, forever. Same storage pattern as psort_items / psort_manual.</summary>
        private static readonly int HomeHash = "psort_home".GetStableHashCode();

        private static readonly HashSet<Sign> Signs = new HashSet<Sign>();
        private static readonly Dictionary<Container, KeyValuePair<float, FilterSpec>> Cache =
            new Dictionary<Container, KeyValuePair<float, FilterSpec>>();

        internal static void RegisterSign(Sign s) { if (s != null) { Signs.Add(s); InvalidateAll(); } }

        /// <summary>Drop one chest's cached spec (chest unloaded, or its own filters changed).</summary>
        internal static void Invalidate(Container c) { if (c != null) Cache.Remove(c); }

        /// <summary>Drop every cached spec — a sign appeared, changed text or was destroyed, and any
        /// chest within SignRange of it may now resolve differently.</summary>
        internal static void InvalidateAll() { Cache.Clear(); }

        // ---------- pinned items (ZDO) ----------

        internal static List<string> GetPinned(Container c)
        {
            var result = new List<string>();
            var nv = SorterZdo.NView(c);
            if (nv == null || !nv.IsValid()) return result;
            var csv = nv.GetZDO().GetString(ItemsHash, "");
            foreach (var raw in csv.Split(','))
            {
                var t = raw.Trim();
                if (t.Length > 0) result.Add(t);
            }
            return result;
        }

        internal static void SetPinned(Container c, IEnumerable<string> tokens)
        {
            var nv = SorterZdo.NView(c);
            if (nv == null || !nv.IsValid()) return;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(ItemsHash, string.Join(",", tokens));
            Cache.Remove(c);
        }

        /// <summary>Capture the distinct item types currently inside as explicit filters.
        /// Returns how many were pinned.</summary>
        internal static int PinContents(Container c)
        {
            var inv = c.GetInventory();
            if (inv == null) return 0;
            var names = new HashSet<string>();
            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_shared == null) continue;
                var n = Names.Normalize(item.m_shared.m_name);
                if (n.Length > 0) names.Add(n);
            }
            if (names.Count > 0) SetPinned(c, names);
            return names.Count;
        }

        internal static void ClearPinned(Container c) => SetPinned(c, new string[0]);

        internal static bool GetManual(Container c)
        {
            var nv = SorterZdo.NView(c);
            return nv != null && nv.IsValid() && nv.GetZDO().GetBool(ManualHash, false);
        }

        internal static void SetManual(Container c, bool manual)
        {
            var nv = SorterZdo.NView(c);
            if (nv == null || !nv.IsValid()) return;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(ManualHash, manual);
            Cache.Remove(c);
        }

        // ---------- claimed bucket home (ZDO) — Organize v2, plan §4.1 ----------

        /// <summary>The bucket key this chest was claimed as a home for, or null.</summary>
        internal static string GetHome(Container c)
        {
            var nv = SorterZdo.NView(c);
            if (nv == null || !nv.IsValid()) return null;
            var v = nv.GetZDO().GetString(HomeHash, "");
            return string.IsNullOrEmpty(v) ? null : v;
        }

        /// <summary>Record that the allocator claimed this chest as <paramref name="bucketKey"/>'s
        /// home. Pass null to clear. Written only for chests the allocator claims ITSELF — pin-, sign-
        /// and station-derived anchors re-derive every run, and marking them would freeze a station
        /// chest's role even after the station is torn down (v2 plan §4.1).</summary>
        internal static void SetHome(Container c, string bucketKey)
        {
            var nv = SorterZdo.NView(c);
            if (nv == null || !nv.IsValid()) return;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(HomeHash, bucketKey ?? "");
            Cache.Remove(c);
        }

        internal static void ClearHome(Container c) => SetHome(c, null);

        // ---------- combined spec (pinned + signs), cached ----------

        internal static FilterSpec GetSpec(Container c)
        {
            if (Cache.TryGetValue(c, out var hit) && Time.time - hit.Key < CacheTtl)
                return hit.Value;

            var spec = new FilterSpec();
            foreach (var pin in GetPinned(c)) spec.Items.Add(pin);
            spec.ManualOnly = GetManual(c);
            spec.Home = GetHome(c);
            ParseNearestSign(c, spec);

            Cache[c] = new KeyValuePair<float, FilterSpec>(Time.time, spec);
            return spec;
        }

        private static void ParseNearestSign(Container c, FilterSpec spec)
        {
            Signs.RemoveWhere(s => s == null);
            var cpos = c.transform.position;
            foreach (var sign in Signs)
            {
                if (Vector3.Distance(sign.transform.position, cpos) > SignRange) continue;
                if (!IsNearestContainerTo(sign.transform.position, c)) continue; // sign belongs to closest chest only

                var text = sign.GetText();
                if (string.IsNullOrEmpty(text)) continue;
                foreach (var line in text.Split('\n'))
                {
                    var l = line.Trim();
                    if (!l.ToLowerInvariant().StartsWith("sort:")) continue;
                    foreach (var raw in l.Substring(5).Split(','))
                    {
                        var t = raw.Trim().ToLowerInvariant();
                        if (t.Length == 0) continue;
                        if (t == "off" || t == "ignore" || t == "none") { spec.Ignore = true; }
                        else if (t.Length >= 2 && t[0] == 'p' && int.TryParse(t.Substring(1), out var p)) { spec.Priority = p; }
                        else if (Groups.IsGroup(t)) { spec.GroupNames.Add(t); }
                        else { spec.Items.Add(t); }
                    }
                }
            }
        }

        private static bool IsNearestContainerTo(Vector3 signPos, Container candidate)
        {
            var nearest = ContainerTracker.NearestTo(signPos, SignRange);
            return nearest == null || nearest == candidate;
        }
    }
}
