using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Jobs;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services.Inference;

namespace QuantAgent.API.Controllers;

[ApiController]
[Route("api/quant")]
public class QuantController : ControllerBase
{
    private readonly QuantDbContext _context;
    private readonly IOllamaInferenceService _inference;
    private readonly ILogger<QuantController> _logger;

    public QuantController(QuantDbContext context, IOllamaInferenceService inference, ILogger<QuantController> logger)
    {
        _context = context;
        _inference = inference;
        _logger = logger;
    }

    /// <summary>
    /// Triggers an immediate daily match ingestion via Hangfire.
    /// Equivalent to the Telegram /ingestar_hoy command.
    /// </summary>
    [HttpPost("ingest")]
    public IActionResult ForceIngest()
    {
        _logger.LogInformation("[Quant] Forced ingestion triggered from dashboard");
        BackgroundJob.Enqueue<MatchIngestionJob>(x => x.ExecuteAsync());
        return Ok(new { message = "Ingesta encolada correctamente." });
    }

    /// <summary>
    /// Manual self-criticism for a single lost prediction.
    /// Loads the prediction, asks Ollama why it failed, and persists
    /// a <see cref="ReglaAprendida"/> so future predictions incorporate
    /// the lesson via RAG context.
    /// </summary>
    [HttpPost("analyze-failure/{predictionId:guid}")]
    public async Task<IActionResult> AnalyzeFailure(Guid predictionId)
    {
        var prediccion = await _context.Predicciones
            .Include(p => p.Partido)
            .FirstOrDefaultAsync(p => p.Id == predictionId);

        if (prediccion is null)
            return NotFound(new { message = "Predicción no encontrada." });

        if (prediccion.Estado != EstadoPrediccion.Perdida)
            return BadRequest(new { message = "Solo se pueden analizar predicciones perdidas." });

        // Idempotency: skip if this prediction was already analyzed
        var alreadyAnalyzed = await _context.ReglasAprendidas
            .AnyAsync(r => r.PrediccionId == predictionId);

        if (alreadyAnalyzed)
            return Conflict(new { message = "Esta predicción ya fue analizada.", reglaExtraida = true });

        var partido = prediccion.Partido
            ?? throw new InvalidOperationException($"Prediccion {predictionId} has no Partido loaded.");

        var resultadoReal = $"{partido.EquipoLocal} {partido.GolesLocal ?? 0}-{partido.GolesVisitante ?? 0} {partido.EquipoVisitante}";

        var prompt = $"Predeciste {prediccion.Seleccion} con cuota {prediccion.Cuota} y este razonamiento: {prediccion.Razonamiento}. "
                   + $"El resultado real fue {resultadoReal}. Fallaste. "
                   + "Analiza el error en tu lógica financiera o deportiva y extrae una sola regla estricta de una oración para no volver a cometerlo. "
                   + "Responde estrictamente en JSON: { \"regla\": \"...\" }";

        _logger.LogInformation(
            "[Quant] Analyzing failure for prediction {PredId}: {Seleccion} vs {Resultado}",
            predictionId, prediccion.Seleccion, resultadoReal);

        ReflectionResult result;
        try
        {
            result = await _inference.GenerateReflectionAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Quant] Ollama reflection failed for prediction {PredId}", predictionId);
            return StatusCode(502, new { message = "Ollama no respondió. Intenta de nuevo más tarde." });
        }

        if (string.IsNullOrWhiteSpace(result.Regla))
        {
            _logger.LogWarning("[Quant] Ollama returned empty rule for prediction {PredId}", predictionId);
            return StatusCode(500, new { message = "Ollama no generó una regla válida." });
        }

        var equipo = prediccion.Seleccion == partido.EquipoLocal
            ? partido.EquipoLocal
            : prediccion.Seleccion == partido.EquipoVisitante
                ? partido.EquipoVisitante
                : "Empate";

        var rule = new ReglaAprendida
        {
            Equipo = equipo,
            Contexto = resultadoReal,
            Regla = result.Regla.Trim(),
            Peso = 1,
            PrediccionId = prediccion.Id,
        };

        _context.ReglasAprendidas.Add(rule);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "[Quant] Saved rule from failure analysis: '{Rule}' (prediction {PredId})",
            rule.Regla, predictionId);

        return Ok(new { message = "Regla extraída correctamente.", regla = rule.Regla });
    }
}
