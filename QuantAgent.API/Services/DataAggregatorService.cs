using QuantAgent.API.Models;
namespace QuantAgent.API.Services;

/// <summary>
/// Orchestrates the primary (API-Football) and secondary (TheOddsAPI)
/// data sources, enriches fixtures with cross-source odds comparison,
/// and provides a single ingestion surface for upstream jobs.
/// <para>
/// Registered as <c>Scoped</c> to match the lifetime of the jobs that
/// consume it. Its dependencies (FootballApiService, etc.) are managed
/// by the DI container's HttpClient pooling.
/// </para>
/// </summary>
public class DataAggregatorService : IDataAggregatorService
{
    private readonly FootballApiService _footballApi;
    private readonly IAlternativeOddsService _alternativeOdds;
    private readonly OddsComparatorService _comparator;
    private readonly ILogger<DataAggregatorService> _logger;

    public DataAggregatorService(
        FootballApiService footballApi,
        IAlternativeOddsService alternativeOdds,
        OddsComparatorService comparator,
        ILogger<DataAggregatorService> logger)
    {
        _footballApi = footballApi;
        _alternativeOdds = alternativeOdds;
        _comparator = comparator;
        _logger = logger;
    }

    /// <summary>
    /// Fetches daily fixtures from API-Football, then concurrently
    /// enriches each fixture with alternative odds from TheOddsAPI
    /// and runs the comparator.
    /// </summary>
    public async Task<List<AggregatedFixtureDto>> GetDailyFixturesAsync(
        DateTime date, int[] leagueIds, CancellationToken ct = default)
    {
        var fixtures = await _footballApi.GetDailyFixturesAsync(date, leagueIds);
        if (fixtures.Count == 0) return [];

        // Enrich with alternative odds in parallel
        var enrichmentTasks = fixtures.Select(dto =>
            EnrichAsync(dto, ct));
        var enriched = await Task.WhenAll(enrichmentTasks);

        return enriched.Where(e => e is not null).Cast<AggregatedFixtureDto>().ToList();
    }

    public async Task<AggregatedFixtureDto?> GetFixtureByIdAsync(
        int fixtureId, CancellationToken ct = default)
    {
        PartidoExternoDto fixture;
        try
        {
            fixture = await _footballApi.GetFixtureByIdAsync(fixtureId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataAggregator: failed to fetch fixture {FixtureId}", fixtureId);
            return null;
        }

        return await EnrichAsync(fixture, ct);
    }

    public async Task<(int GolesLocal, int GolesVisitante, Models.Enums.EstadoPartido Estado)>
        GetMatchResultAsync(int fixtureId)
    {
        return await _footballApi.GetMatchResultAsync(fixtureId);
    }

    // ---------- enrichment helpers --------------------------------------------

    private async Task<AggregatedFixtureDto?> EnrichAsync(
        PartidoExternoDto dto, CancellationToken ct)
    {
        try
        {
            var altOdds = await _alternativeOdds.GetMatchWinnerOddsAsync(
                dto.EquipoLocal, dto.EquipoVisitante, dto.LeagueId, ct);

            if (altOdds.Count == 0)
            {
                // No alternative odds available — return unevaluated
                return new AggregatedFixtureDto(dto);
            }

            // Fetch primary (Bet365) odds for comparison
            var (cuotaLocal, cuotaEmpate, cuotaVisita) =
                await _footballApi.GetMatchOddsAsync(dto.FixtureId);

            if (cuotaLocal <= 0 || cuotaEmpate <= 0 || cuotaVisita <= 0)
            {
                // Primary odds not available yet — skip comparison
                return new AggregatedFixtureDto(dto);
            }

            var report = _comparator.Compare(
                dto.EquipoLocal, dto.EquipoVisitante,
                cuotaLocal, cuotaEmpate, cuotaVisita,
                altOdds);

            if (report.Signals.Count > 0)
            {
                _logger.LogWarning(
                    "DataAggregator: {Count} arbitrage/data-quality signals for " +
                    "{Home} vs {Away} (fixture {FixtureId})",
                    report.Signals.Count, dto.EquipoLocal, dto.EquipoVisitante, dto.FixtureId);
            }

            return new AggregatedFixtureDto(dto, report);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DataAggregator: enrichment failed for fixture {FixtureId} ({Home} vs {Away})",
                dto.FixtureId, dto.EquipoLocal, dto.EquipoVisitante);
            return new AggregatedFixtureDto(dto);
        }
    }
}
