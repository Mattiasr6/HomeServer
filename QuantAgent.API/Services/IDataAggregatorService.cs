using QuantAgent.API.Models;

namespace QuantAgent.API.Services;

/// <summary>
/// Unified data coordinator that abstracts over multiple external
/// data sources (API-Football, TheOddsAPI, SoccerStats, etc.).
/// <para>
/// Replaces direct <c>FootballApiService</c> calls in
/// <c>MatchIngestionJob</c> so the ingestion pipeline treats all
/// sources as a single virtual feed.
/// </para>
/// </summary>
public interface IDataAggregatorService
{
    /// <summary>
    /// Returns today's fixtures from the primary source (API-Football),
    /// enriched with alternative odds where available.
    /// </summary>
    Task<List<AggregatedFixtureDto>> GetDailyFixturesAsync(
        DateTime date, int[] leagueIds, CancellationToken ct = default);

    /// <summary>
    /// Returns a specific fixture with enriched odds comparison data.
    /// </summary>
    Task<AggregatedFixtureDto?> GetFixtureByIdAsync(
        int fixtureId, CancellationToken ct = default);

    /// <summary>
    /// Fetches match result (goals, status) from the primary source.
    /// </summary>
    Task<(int GolesLocal, int GolesVisitante, Models.Enums.EstadoPartido Estado)>
        GetMatchResultAsync(int fixtureId);
}

/// <summary>
/// A fixture with its odds enriched from secondary sources.
/// Carries the comparison report so downstream jobs can react to
/// arbitrage or data-inconsistency signals.
/// </summary>
public record AggregatedFixtureDto(
    PartidoExternoDto Fixture,
    OddsComparisonReport? OddsComparison = null);
