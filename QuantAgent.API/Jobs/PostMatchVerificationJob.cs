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
    public async Task VerifyMatchAsync(Guid partidoId)
    {
        _logger.LogInformation("[Phase B] Verifying match {Id}", partidoId);

        var partido = await _db.Partidos.FindAsync(partidoId);
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

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[Phase B] Match {Id} finalized: {Local} {Gl}-{Gv} {Visitante}",
            partido.Id, partido.EquipoLocal, partido.GolesLocal, partido.GolesVisitante, partido.EquipoVisitante);

        // --- Evaluate prediction outcome ----------------------------------------
        var prediccion = await _db.Predicciones
            .FirstOrDefaultAsync(p => p.PartidoId == partido.Id);

        if (prediccion is not null)
        {
            bool ganada;
            if (prediccion.Seleccion == partido.EquipoLocal)
                ganada = partido.GolesLocal > partido.GolesVisitante;
            else if (prediccion.Seleccion == partido.EquipoVisitante)
                ganada = partido.GolesVisitante > partido.GolesLocal;
            else // "Empate"
                ganada = partido.GolesLocal == partido.GolesVisitante;

            prediccion.Estado = ganada ? EstadoPrediccion.Ganada : EstadoPrediccion.Perdida;
            prediccion.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "[Phase B] Prediction {PredId}: {Seleccion} → {Result}",
                prediccion.Id, prediccion.Seleccion, prediccion.Estado);
        }

        // Enqueue self-reflection to learn from any losses
        BackgroundJob.Enqueue<SelfReflectionJob>(j => j.ReflectOnLossesAsync());
    }
}
