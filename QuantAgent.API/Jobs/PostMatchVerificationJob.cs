using Hangfire;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services;

namespace QuantAgent.API.Jobs;

/// <summary>
/// Phase B of the orchestration loop: once a match is scheduled to
/// have ended, fetch the real result from API-Football and transition
/// the partido to <see cref="EstadoPartido.Finalizado"/>.
/// If the fixture hasn't finished yet, the job throws so Hangfire
/// retries with exponential backoff.
/// <para>
/// Evaluates ALL predictions for the match (Ganador, Goles markets).
/// Corners market evaluation requires separate /fixtures/statistics API call
/// and is deferred until that data is available — predictions remain Pendiente.
/// </para>
/// </summary>
public class PostMatchVerificationJob
{
    private readonly QuantDbContext _db;
    private readonly FootballApiService _footballApi;
    private readonly ILogger<PostMatchVerificationJob> _logger;

    public PostMatchVerificationJob(
        QuantDbContext db, FootballApiService footballApi,
        ILogger<PostMatchVerificationJob> logger)
    {
        _db = db;
        _footballApi = footballApi;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 30, 60, 120, 300 })]
    public async Task VerifyMatchAsync(Guid partidoId, CancellationToken ct = default)
    {
        _logger.LogInformation("[Phase B] Verifying match {Id}", partidoId);

        var partido = await _db.Partidos.FindAsync(new object[] { partidoId }, ct);
        if (partido is null)
        {
            _logger.LogWarning("[Phase B] Match {Id} not found — skipping", partidoId);
            return;
        }

        if (partido.Estado == EstadoPartido.Finalizado)
        {
            _logger.LogInformation("[Phase B] Match {Id} already finalized — idempotent skip", partidoId);
            return;
        }

        // --- Get real result from API-Football --------------------------------
        var fixtureId = partido.FixtureId
            ?? throw new InvalidOperationException(
                $"Match {partidoId} has no FixtureId — cannot fetch real result.");

        var (golesLocal, golesVisitante, estado) = await _footballApi.GetMatchResultAsync(fixtureId);

        partido.Estado = estado;
        partido.GolesLocal = golesLocal;
        partido.GolesVisitante = golesVisitante;
        partido.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[Phase B] Match {Id} finalized: {Local} {Gl}-{Gv} {Visitante}",
            partido.Id, partido.EquipoLocal, partido.GolesLocal, partido.GolesVisitante, partido.EquipoVisitante);

        // --- Evaluate ALL prediction outcomes for this match ------------------
        var predicciones = await _db.Predicciones
            .Where(p => p.PartidoId == partido.Id)
            .ToListAsync(ct);

        if (predicciones.Count == 0)
        {
            _logger.LogInformation("[Phase B] No predictions found for match {Id}", partidoId);
            return;
        }

        var evaluated = 0;
        var skippedCorners = 0;
        var hasLoss = false;

        foreach (var prediccion in predicciones)
        {
            if (prediccion.Estado != EstadoPrediccion.Pendiente)
            {
                _logger.LogDebug(
                    "[Phase B] Prediction {PredId} already {Estado} — skipping",
                    prediccion.Id, prediccion.Estado);
                continue;
            }

            switch (prediccion.Mercado)
            {
                case TipoMercado.Ganador:
                    prediccion.Estado = EvaluateGanador(prediccion, partido);
                    prediccion.UpdatedAt = DateTime.UtcNow;
                    evaluated++;
                    break;

                case TipoMercado.Goles:
                    prediccion.Estado = EvaluateGoles(prediccion, partido);
                    prediccion.UpdatedAt = DateTime.UtcNow;
                    evaluated++;
                    break;

                case TipoMercado.Corners:
                    // Corners evaluation needs actual corners statistics from
                    // API-Football /fixtures/statistics endpoint, which is not
                    // yet implemented in FootballApiService. Keep as Pendiente
                    // and log so we can track the gap.
                    _logger.LogWarning(
                        "[Phase B] Prediction {PredId} (Corners) for match {Id} cannot be evaluated " +
                        "— no corners statistics available from API yet. Remains Pendiente.",
                        prediccion.Id, partidoId);
                    skippedCorners++;
                    break;
            }

            if (prediccion.Estado == EstadoPrediccion.Perdida)
                hasLoss = true;

            _logger.LogInformation(
                "[Phase B] Prediction {PredId} market={Market} sel={Sel} → {Result}",
                prediccion.Id, prediccion.Mercado, prediccion.Seleccion, prediccion.Estado);
        }

        if (evaluated > 0 || skippedCorners > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[Phase B] Done for match {Id}: evaluated={Evaluated} corners_skipped={CornersSkipped}",
                partidoId, evaluated, skippedCorners);
        }

        // Enqueue self-reflection to learn from any losses
        if (hasLoss)
        {
            BackgroundJob.Enqueue<SelfReflectionJob>(j => j.ReflectOnLossesAsync(default));
        }
    }

    /// <summary>
    /// Evaluates a Ganador (Match Winner) prediction against the actual score.
    /// </summary>
    private static EstadoPrediccion EvaluateGanador(Prediccion prediccion, Partido partido)
    {
        if (prediccion.Seleccion == partido.EquipoLocal)
            return partido.GolesLocal > partido.GolesVisitante
                ? EstadoPrediccion.Ganada
                : EstadoPrediccion.Perdida;

        if (prediccion.Seleccion == partido.EquipoVisitante)
            return partido.GolesVisitante > partido.GolesLocal
                ? EstadoPrediccion.Ganada
                : EstadoPrediccion.Perdida;

        // "Empate" or unknown selection
        return partido.GolesLocal == partido.GolesVisitante
            ? EstadoPrediccion.Ganada
            : EstadoPrediccion.Perdida;
    }

    /// <summary>
    /// Evaluates a Goles (Over/Under goals) prediction against actual total goals.
    /// TotalGoals threshold is set by the inference service (typically 2.5).
    /// </summary>
    private static EstadoPrediccion EvaluateGoles(Prediccion prediccion, Partido partido)
    {
        if (prediccion.TotalGoals <= 0)
        {
            // No threshold stored — cannot evaluate fairly
            return EstadoPrediccion.Pendiente;
        }

        var totalGoals = partido.GolesLocal + partido.GolesVisitante;
        var isOver = string.Equals(prediccion.Seleccion, "Over", StringComparison.OrdinalIgnoreCase);

        if (isOver)
            return totalGoals > prediccion.TotalGoals
                ? EstadoPrediccion.Ganada
                : EstadoPrediccion.Perdida;
        else
            return totalGoals <= prediccion.TotalGoals
                ? EstadoPrediccion.Ganada
                : EstadoPrediccion.Perdida;
    }
}
