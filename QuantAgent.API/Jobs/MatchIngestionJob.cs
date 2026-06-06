using Microsoft.Extensions.Configuration;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services;

namespace QuantAgent.API.Jobs;

/// <summary>
/// Phase A of the orchestration loop: ingest matches from the
/// upstream feed (currently simulated) and schedule the Phase B
/// verification job to fire after the match ends.
/// </summary>
public class MatchIngestionJob
{
    private readonly QuantDbContext _db;
    private readonly IDataAggregatorService _dataAggregator;
    private readonly IConfiguration _config;
    private readonly ILogger<MatchIngestionJob> _logger;
    private readonly ITelemetryService _telemetry;
    public MatchIngestionJob(
        QuantDbContext db, IDataAggregatorService dataAggregator,
        IConfiguration config,
        ITelemetryService telemetry,
        ILogger<MatchIngestionJob> logger)
    {
        _db = db;
        _dataAggregator = dataAggregator;
        _config = config;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>
    /// Fetches today's real fixtures from API-Football for the configured
    /// leagues (ActiveLeagues from appsettings), persists them,
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("[Phase A] Starting daily match ingestion at {Time:o}", DateTime.UtcNow);

        await _telemetry.BroadcastLogAsync("Iniciando ingesta diaria de partidos...", "INFO");
        var leagues = _config.GetSection("ActiveLeagues").Get<int[]>();
        if (leagues is null || leagues.Length == 0)
        {
            _logger.LogWarning("[Phase A] No ActiveLeagues configured — using defaults [1,39,140,10,13,2]");
            leagues = [1, 39, 140, 10, 13, 2];
        }

        // Idempotency pre-check: skip API call entirely if today's matches are already ingested
        // Prevents unnecessary API-Football billing when the cron fires multiple times
        var todayStart = DateTime.UtcNow.Date;
        var alreadyIngested = await _db.Partidos
            .AnyAsync(p => p.FechaInicio >= todayStart);

        if (alreadyIngested)
        {
            _logger.LogInformation(
                "[Phase A] Today's fixtures already ingested — skipping API call to avoid unnecessary billing.");
            // Even if ingestion is skipped, still enqueue value-bet analysis for any pending matches
            var pendingCount = await _db.Partidos
                .CountAsync(p => p.Estado == EstadoPartido.Pendiente
                    && !_db.Predicciones.Any(pr => pr.PartidoId == p.Id));
            if (pendingCount > 0)
            {
                BackgroundJob.Enqueue<ValueBetDetectionJob>(
                    x => x.AnalyzePendingMatchesAsync(CancellationToken.None));
                _logger.LogInformation(
                    "[Phase A] {PendingCount} pending matches — enqueued value-bet analysis.", pendingCount);
            }
            return;
        }

        var fixtures = await _dataAggregator.GetDailyFixturesAsync(
            DateTime.UtcNow, leagues, CancellationToken.None);

        if (fixtures.Count == 0)
        {
            _logger.LogInformation("[Phase A] No fixtures found for today in target leagues.");
            return;
        }
        await _telemetry.BroadcastLogAsync(string.Format(
            "Obtenidos {0} partidos de API-Football para {1} ligas",
            fixtures.Count, leagues.Length), "INFO");

        var createdCount = 0;
        var skippedCount = 0;

        foreach (var aggregated in fixtures)
        {
            var dto = aggregated.Fixture;
            // Idempotency: skip if already ingested
            var exists = await _db.Partidos.AnyAsync(p => p.FixtureId == dto.FixtureId);
            if (exists)
            {
                skippedCount++;
                continue;
            }

            var partido = new Partido
            {
                FixtureId = dto.FixtureId,
                EquipoLocal = dto.EquipoLocal,
                EquipoVisitante = dto.EquipoVisitante,
                FechaInicio = dto.FechaInicio,
                Estado = EstadoPartido.Pendiente
            };

            _db.Partidos.Add(partido);
            await _db.SaveChangesAsync();
            createdCount++;

            // Schedule Phase B to run 130 minutes after real kick-off
            var scheduledAt = dto.FechaInicio.AddMinutes(130);
            var jobId = BackgroundJob.Schedule<PostMatchVerificationJob>(
                x => x.VerifyMatchAsync(partido.Id), scheduledAt);

            _logger.LogInformation(
                "[Phase A] Ingested fixture {FixtureId}: {Local} vs {Visitante} @ {KickOff:o} | Verification job {JobId} @ {ScheduledAt:o}",
                dto.FixtureId, dto.EquipoLocal, dto.EquipoVisitante,
                dto.FechaInicio, jobId, scheduledAt);
        }

        _logger.LogInformation(
            "[Phase A] Done. Created={Created} Skipped(already-existing)={Skipped} Total(API)={Total}",
            createdCount, skippedCount, fixtures.Count);

        // Phase C: enqueue value-bet analysis immediately
        var analysisJobId = BackgroundJob.Enqueue<ValueBetDetectionJob>(
            x => x.AnalyzePendingMatchesAsync(CancellationToken.None));

        _logger.LogInformation(
            "[Phase A] Enqueued value-bet analysis job {JobId}", analysisJobId);
        await _telemetry.BroadcastLogAsync(string.Format(
            "Ingesta completada: {0} creados, {1} omitidos, {2} total",
            createdCount, skippedCount, fixtures.Count), "INFO");
    }

}