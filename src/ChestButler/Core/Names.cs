using System;
using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>Item-name normalization (Smarter Containers convention):
    /// "$item_trophy_boar" → "trophyboar". All filters/groups match on normalized names.
    ///
    /// 1.1.2: every comparison is ORDINAL (was culture-sensitive, which is both ~4x slower and
    /// locale-dependent for what is a pure token matcher), wildcard tokens are parsed once instead
    /// of allocating a Trim('*') per comparison, and Normalize memoizes on the raw shared name.
    /// At 400 chests these three paths ran millions of times per plan.</summary>
    internal static class Names
    {
        // Bounded by the number of distinct item prefabs in the world (~200 vanilla, a few hundred
        // more with content mods) — Normalize is a pure function of its input string.
        private static readonly Dictionary<string, string> NormCache = new Dictionary<string, string>(512);

        private struct Token
        {
            public string Core;      // the token with '*' stripped
            public bool Leading;     // '*' at the start
            public bool Trailing;    // '*' at the end
        }

        private static readonly Dictionary<string, Token> TokenCache = new Dictionary<string, Token>(256);

        internal static string Normalize(string sharedName)
        {
            if (string.IsNullOrEmpty(sharedName)) return string.Empty;
            if (NormCache.TryGetValue(sharedName, out var hit)) return hit;

            var s = sharedName;
            if (s.StartsWith("$item_", StringComparison.Ordinal)) s = s.Substring(6);
            else if (s[0] == '$') s = s.Substring(1);
            var result = s.Replace("_", "").Trim().ToLowerInvariant();

            NormCache[sharedName] = result;
            return result;
        }

        /// <summary>Token match with optional leading/trailing '*' wildcards
        /// ("trophy*", "*meat", "*mushroom*", or exact).</summary>
        internal static bool Matches(string token, string normName)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(normName)) return false;

            if (!TokenCache.TryGetValue(token, out var t))
            {
                t = new Token
                {
                    Leading = token[0] == '*',
                    Trailing = token[token.Length - 1] == '*',
                    Core = token.Trim('*'),
                };
                TokenCache[token] = t;
            }

            if (t.Core.Length == 0) return false;
            if (t.Leading && t.Trailing) return normName.IndexOf(t.Core, StringComparison.Ordinal) >= 0;
            if (t.Trailing) return normName.StartsWith(t.Core, StringComparison.Ordinal);
            if (t.Leading) return normName.EndsWith(t.Core, StringComparison.Ordinal);
            return string.Equals(normName, t.Core, StringComparison.Ordinal);
        }
    }
}
