using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChestButler.Core
{
    /// <summary>One sign's parsed text. <see cref="AreaOffRadius"/> &gt; 0 means the sign's
    /// <c>off</c> applies to EVERY chest within that many metres of the sign, not just the one
    /// nearest chest the classic binding rule picks.</summary>
    internal struct SignSpec
    {
        public List<string> Tokens;     // sort:-line tokens, trimmed + lowercased, in order
        public bool HasOff;
        public float AreaOffRadius;     // 0 = classic single-chest sign
    }

    /// <summary>The sign text grammar, split out of Filters so it is a pure function the offline
    /// suite can reach (same reasoning as GatherMath / BucketKeys: the off-by-ones live in the
    /// parsing, the Unity glue just carries positions around).
    ///
    /// Grammar:
    ///   sort: token, token, ...     one or more lines; tokens are groups, item names ('*'
    ///                               wildcards), pN = priority, off/ignore/none = ignore chest
    ///   NUMBER                      a line that is just a number, OR a bare numeric token on a
    ///                               sort: line - the AREA radius (m) for this sign's 'off'.
    ///                               Only meaningful when the sign also says off; without off a
    ///                               numeric token keeps its historical meaning (an inert item
    ///                               token) so no existing sign changes behaviour.
    ///
    /// So the owner's requested form works verbatim:
    ///     sort: off
    ///     10
    /// = every chest within 10 m of this sign is ignored - not read, not filled.</summary>
    internal static class SignGrammar
    {
        /// <summary>Half a zone. Big enough for any legitimate vault room; small enough that one
        /// sign cannot quietly disable a neighbour's base across a border.</summary>
        internal const float MaxAreaRadius = 32f;

        internal static SignSpec Parse(string text)
        {
            var spec = new SignSpec { Tokens = new List<string>(), AreaOffRadius = 0f };
            if (string.IsNullOrEmpty(text)) return spec;

            foreach (var line in text.Split('\n'))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;

                if (l.ToLowerInvariant().StartsWith("sort:"))
                {
                    foreach (var raw in l.Substring(5).Split(','))
                    {
                        var t = raw.Trim().ToLowerInvariant();
                        if (t.Length == 0) continue;
                        if (t == "off" || t == "ignore" || t == "none") { spec.HasOff = true; continue; }
                        if (TryParseRadius(t, out var r)) { spec.AreaOffRadius = r; continue; }
                        spec.Tokens.Add(t);
                    }
                }
                else if (TryParseRadius(l, out var radius))
                {
                    // A standalone number line. Order-independent: "10" above or below the
                    // "sort: off" line reads the same, because signs are edited in-place and
                    // players should not have to care which line the game kept first.
                    spec.AreaOffRadius = radius;
                }
                // any other line is prose (players label signs); ignore it
            }

            // A radius without off is meaningless - drop it so a sign reading "sort: wood" plus
            // a decorative "42" line does not silently become an area-off sign later when someone
            // adds 'off' to a DIFFERENT line... it does, and that is exactly what they asked for.
            // What must NOT happen is the number alone ignoring anything.
            if (!spec.HasOff) spec.AreaOffRadius = 0f;

            return spec;
        }

        private static bool TryParseRadius(string s, out float radius)
        {
            radius = 0f;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
            if (v <= 0f || float.IsNaN(v) || float.IsInfinity(v)) return false;
            radius = Math.Min(v, MaxAreaRadius);
            return true;
        }
    }
}
