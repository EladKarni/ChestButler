namespace ChestButler.Core
{
    /// <summary>Item-name normalization (Smarter Containers convention):
    /// "$item_trophy_boar" → "trophyboar". All filters/groups match on normalized names.</summary>
    internal static class Names
    {
        internal static string Normalize(string sharedName)
        {
            if (string.IsNullOrEmpty(sharedName)) return string.Empty;
            var s = sharedName;
            if (s.StartsWith("$item_")) s = s.Substring(6);
            else if (s.StartsWith("$")) s = s.Substring(1);
            return s.Replace("_", "").Trim().ToLowerInvariant();
        }

        /// <summary>Token match with optional leading/trailing '*' wildcards
        /// ("trophy*", "*meat", "*mushroom*", or exact).</summary>
        internal static bool Matches(string token, string normName)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(normName)) return false;
            bool star0 = token[0] == '*';
            bool star1 = token[token.Length - 1] == '*';
            var core = token.Trim('*');
            if (core.Length == 0) return false;
            if (star0 && star1) return normName.Contains(core);
            if (star1) return normName.StartsWith(core);
            if (star0) return normName.EndsWith(core);
            return normName == core;
        }
    }
}
