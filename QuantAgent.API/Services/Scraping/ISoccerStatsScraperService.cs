namespace QuantAgent.API.Services.Scraping;

/// <summary>
/// Fetches and caches consolidated league-team statistics from
/// SoccerStats.com for use as RAG context by the inference layer.
/// <para>
/// Implementations are responsible for fan-out across the site's
/// three tabular pages (<c>latest.asp</c>, <c>table.asp?tid=c</c>,
/// <c>table.asp?tid=cr</c>) and for merging the rows by team name.
/// </para>
/// </summary>
internal interface ISoccerStatsScraperService
{
    /// <summary>
    /// Returns the consolidated stats for <paramref name="teamName"/>
    /// within the given <paramref name="league"/> slug, or
    /// <c>null</c> if the team cannot be located in the scraped
    /// tables (e.g. misspelled name, mid-season promotion).
    /// </summary>
    Task<TeamStatsDto?> GetTeamStatsAsync(
        string teamName,
        string league,
        CancellationToken cancellationToken = default);
}
