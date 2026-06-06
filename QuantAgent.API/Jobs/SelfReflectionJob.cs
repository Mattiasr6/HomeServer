using Hangfire;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services.Inference;
using QuantAgent.API.Services.Scraping;

namespace QuantAgent.API.Jobs;

/// <summary>
/// Auto-reflection engine: for every lost prediction, asks Ollama to
/// generate a technical rule explaining why the model was wrong, and
/// persists it to <see cref="ReglaAprendida"/> so future predictions
/// incorporate the lesson via RAG context.
/// </summary>
internal class SelfReflectionJob
{
    private readonly QuantDbContext _db;
    private readonly IOllamaInferenceService _inference;
    private readonly ISoccerStatsScraperService _scraper;
    private readonly ILogger<SelfReflectionJob> _logger;

    private static readonly string[] AllLeagues = ["spain", "england", "germany", "italy", "france"];

    public SelfReflectionJob(
        QuantDbContext db,
        IOllamaInferenceService inference,
        ISoccerStatsScraperService scraper,
        ILogger<SelfReflectionJob> logger)
    {
        _db = db;
        _inference = inference;
        _scraper = scraper;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ReflectOnLossesAsync()
    {
        _logger.LogInformation("[Reflection] Starting auto-reflection for unanalyzed losses");

        // Load all perdida predicciones with their match data
        var losses = await _db.Predicciones
            .Include(p => p.Partido)
            .Where(p => p.Estado == EstadoPrediccion.Perdida)
            .ToListAsync();

        // Exclude predicciones already analyzed (have a ReglaAprendida referencing them)
        var analyzedIds = await _db.ReglasAprendidas
            .Where(r => r.PrediccionId != null)
            .Select(r => r.PrediccionId!.Value)
            .ToListAsync();

        var unanalyzed = losses.Where(p => !analyzedIds.Contains(p.Id)).ToList();

        if (unanalyzed.Count == 0)
        {
            _logger.LogInformation("[Reflection] No unanalyzed losses found");
            return;
        }

        _logger.LogInformation("[Reflection] Found {Count} unanalyzed loss(es)", unanalyzed.Count);

        foreach (var prediccion in unanalyzed)
        {
            await AnalyzeLossAsync(prediccion);
        }

        _logger.LogInformation("[Reflection] Done. Analyzed {Count} loss(es)", unanalyzed.Count);
    }

    private async Task AnalyzeLossAsync(Prediccion prediccion)
    {
        var partido = prediccion.Partido
            ?? throw new InvalidOperationException(
                $"Prediccion {prediccion.Id} has no Partido loaded.");

        // Determine which team this prediction was about
        var equipo = prediccion.Seleccion == partido.EquipoLocal
            ? partido.EquipoLocal
            : prediccion.Seleccion == partido.EquipoVisitante
                ? partido.EquipoVisitante
                : "Empate";

        // Fetch current stats for RAG context (scraper cache serves fast)
        var statsContext = await FetchStatsForTeamAsync(equipo);

        var reflectionPrompt = BuildReflectionPrompt(prediccion, partido, statsContext);

        ReflectionResult result;
        try
        {
            result = await _inference.GenerateReflectionAsync(reflectionPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Reflection] Ollama reflection failed for prediction {PredId}", prediccion.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Regla))
        {
            _logger.LogWarning(
                "[Reflection] Ollama returned empty rule for prediction {PredId}", prediccion.Id);
            return;
        }

        var rule = new ReglaAprendida
        {
            Equipo = equipo,
            Contexto = $"{partido.EquipoLocal} {partido.GolesLocal}-{partido.GolesVisitante} {partido.EquipoVisitante}",
            Regla = result.Regla.Trim(),
            Peso = 1,
            PrediccionId = prediccion.Id
        };

        _db.ReglasAprendidas.Add(rule);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[Reflection] Saved rule for {Team}: '{Rule}' (from prediction {PredId})",
            equipo, rule.Regla, prediccion.Id);
    }

    private async Task<string> FetchStatsForTeamAsync(string teamName)
    {
        foreach (var league in AllLeagues)
        {
            var stats = await _scraper.GetTeamStatsAsync(teamName, league);
            if (stats is not null)
            {
                return $"Pos:{stats.Posicion} Pts:{stats.Puntos} GF:{stats.GolesFavor} GC:{stats.GolesContra} Over25:{stats.Over25}";
            }
        }
        return string.Empty;
    }

    private static string BuildReflectionPrompt(
        Prediccion prediccion, Partido partido, string statsContext)
    {
        return "Eres un algoritmo cuantitativo estricto.\n"
            + "Predijimos que ganaba " + prediccion.Seleccion
            + ", pero el resultado real fue "
            + partido.EquipoLocal + " " + partido.GolesLocal + " - " + partido.GolesVisitante + " " + partido.EquipoVisitante
            + ".\n"
            + "Estadísticas disponibles: " + (string.IsNullOrWhiteSpace(statsContext) ? "(ninguna)" : statsContext)
            + "\n"
            + "DEBES extraer una regla matemática en pseudo-código usando las variables disponibles.\n"
            + "PROHIBIDO usar lenguaje natural — solo operadores lógicos/matemáticos.\n"
            + "Variables disponibles: [PosicionLocal, PuntosLocal, GF_Local, GC_Local, Over25_Local, CornersLocalLocal, CornersLocalVisitante] y equivalentes para Visitante.\n"
            + "Operadores permitidos: >, <, >=, <=, ==, AND, OR, NOT, =>, -, +\n"
            + "Ejemplo de formato requerido: [PosicionLocal > 5] AND [GF_Local < 1.5] => IGNORAR\n"
            + "\n"
            + "Responde estrictamente en JSON: { \"regla\": \"...\" }";
}

}