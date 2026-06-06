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
    private readonly FootballApiService _footballApi;
    private readonly IConfiguration _config;
    private readonly ILogger<MatchIngestionJob> _logger;
    public MatchIngestionJob(
        QuantDbContext db, FootballApiService footballApi,
        IConfiguration config,
        ILogger<MatchIngestionJob> logger)
    {
        _db = db;
        _footballApi = footballApi;
        _config = config;
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

        var leagues = _config.GetSection("ActiveLeagues").Get<int[]>();
        if (leagues is null || leagues.Length == 0)
        {
            _logger.LogWarning("[Phase A] No ActiveLeagues configured — using defaults [1,39,140,10,13,2]");
            leagues = [1, 39, 140, 10, 13, 2];
        }

        var fixtures = await _footballApi.GetDailyFixturesAsync(
            DateTime.UtcNow, leagues);

        if (fixtures.Count == 0)
        {
            _logger.LogInformation("[Phase A] No fixtures found for today in target leagues.");
            return;
        }

        var createdCount = 0;
        var skippedCount = 0;

        foreach (var dto in fixtures)
        {
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
    }

}