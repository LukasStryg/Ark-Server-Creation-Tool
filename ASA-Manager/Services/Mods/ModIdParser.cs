using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ARKServerCreationTool.Services.Mods
{
    public static class ModIdParser
    {
        private static readonly Regex Number = new(@"\d+", RegexOptions.Compiled);

        /// <summary>Extracts distinct mod ids (in first-seen order) from free text: bare ids or URLs containing a numeric id.</summary>
        public static IReadOnlyList<ulong> ParseMany(string text)
        {
            var result = new List<ulong>();
            var seen = new HashSet<ulong>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (var token in text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseOne(token, out ulong id) && seen.Add(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>True if the token is a bare numeric id or a URL whose LAST numeric segment is the id.</summary>
        public static bool TryParseOne(string token, out ulong id)
        {
            id = 0;
            token = token.Trim();
            if (ulong.TryParse(token, out id)) return true;

            // URL/other: only accept if it contains a numeric segment (slug-only ASA URLs will not).
            var matches = Number.Matches(token);
            if (matches.Count == 0) return false;
            return ulong.TryParse(matches[matches.Count - 1].Value, out id);
        }
    }
}
