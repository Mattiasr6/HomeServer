namespace QuantAgent.API.Services.Scraping;

/// <summary>
/// Resolves team-name discrepancies between data sources (DB / API fixtures)
/// and the names as they appear on SoccerStats.com.
/// <para>
/// First checks a curated alias map for known cross-source differences
/// (e.g. "Barcelona" / "FC Barcelona", "PSG" / "Paris Saint Germain");
/// if no alias is found, falls back to a case-insensitive substring match
/// (<c>candidate.Contains(rawName) OR rawName.Contains(candidate)</c>).
/// </para>
/// </summary>
internal static class TeamNameNormalizer
{
    /// <summary>
    /// Known cross-source aliases. Key = name as it appears in the DB or
    /// incoming fixture API; Value = name as it appears on SoccerStats.com.
    /// Case-insensitive.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // --- La Liga ---
            ["Barcelona"] = "FC Barcelona",
            ["Barça"] = "FC Barcelona",
            ["Barca"] = "FC Barcelona",
            ["Real Madrid"] = "Real Madrid CF",
            ["Real Madrid CF"] = "Real Madrid CF",
            ["Atlético"] = "Atlético Madrid",
            ["Atlético de Madrid"] = "Atlético Madrid",
            ["Atletico Madrid"] = "Atlético Madrid",
            ["Atletico de Madrid"] = "Atlético Madrid",
            // --- Premier League ---
            ["Manchester Utd"] = "Manchester United",
            ["Manchester United"] = "Manchester United",
            ["Man Utd"] = "Manchester United",
            ["Man United"] = "Manchester United",
            ["Newcastle Utd"] = "Newcastle",
            ["Newcastle United"] = "Newcastle",
            ["Tottenham Hotspur"] = "Tottenham",
            ["Spurs"] = "Tottenham",
            // --- Serie A ---
            ["Inter"] = "Inter Milan",
            ["Inter de Milán"] = "Inter Milan",
            ["Inter de Milan"] = "Inter Milan",
            ["AC Milan"] = "Milan",
            ["Milan"] = "Milan",
            // --- Ligue 1 ---
            ["PSG"] = "Paris Saint Germain",
            ["Paris Saint-Germain"] = "Paris Saint Germain",
            ["Paris St Germain"] = "Paris Saint Germain",
            // --- Bundesliga ---
            ["Bayern"] = "Bayern Munich",
            ["Bayern de Múnich"] = "Bayern Munich",
            ["Bayern de Munchen"] = "Bayern Munich",
        };

    /// <summary>
    /// Given a raw team name (e.g. from the DB), tries to find a matching
    /// name in the provided set of cached team names (e.g. from SoccerStats).
    /// Returns <c>null</c> if no acceptable match is found.
    /// </summary>
    public static string? FindMatch(string rawName, IReadOnlyCollection<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(rawName) || candidates == null || candidates.Count == 0)
            return null;

        // 1. Direct alias → try to find the alias value in candidates.
        if (KnownAliases.TryGetValue(rawName, out var aliasTarget))
        {
            var match = candidates.FirstOrDefault(c =>
                string.Equals(c, aliasTarget, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;

            // Alias value wasn't in cache keys; fall through to substring matching
            // using the alias target as the search key.
            var fuzzyAlias = candidates.FirstOrDefault(c =>
                c.Contains(aliasTarget, StringComparison.OrdinalIgnoreCase) ||
                aliasTarget.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (fuzzyAlias != null)
                return fuzzyAlias;
        }

        // 2. Substring / fuzzy: candidate contains raw name, or vice versa.
        //    Prefer the candidate-that-contains-raw direction (more specific).
        return candidates.FirstOrDefault(c =>
            c.Contains(rawName, StringComparison.OrdinalIgnoreCase) ||
            rawName.Contains(c, StringComparison.OrdinalIgnoreCase));
    }
}
