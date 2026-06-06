using QuantAgent.API.Models;
using QuantAgent.API.Services.Scraping;

namespace QuantAgent.API.Services.Inference;

/// <summary>
/// Quantitative inference abstraction. Produces a structured
/// <see cref="PrediccionResult"/> for a given match by combining
/// the raw match data with the agent's accumulated learned rules
/// and (optionally) real-time team statistics from SoccerStats.com
/// used as RAG context.
/// </summary>
public interface IOllamaInferenceService
{
    /// <summary>
    /// Analyze <paramref name="partido"/> using the local LLM, the
    /// <paramref name="reglasEquipo"/> the agent has learned for the
    /// involved teams, live statistics (when available), and market
    /// odds from Bet365. Returns a deterministic decision, confidence
    /// score (0-100) and rationale.
    /// </summary>
    Task<PrediccionResult> AnalyzeMatchAsync(
        Partido partido,
        List<ReglaAprendida> reglasEquipo,
        TeamStatsDto? localStats,
        TeamStatsDto? visitanteStats,
        decimal cuotaLocal,
        decimal cuotaEmpate,
        decimal cuotaVisita,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ask Ollama to self-criticize a wrong prediction and produce
    /// a concise rule for why it failed and how to avoid it in future.
    /// </summary>
    Task<ReflectionResult> GenerateReflectionAsync(
        string prompt,
        CancellationToken cancellationToken = default);
    }

/// <summary>
/// Quantitative decision produced by the inference layer.
/// <see cref="Decision"/>: "APOSTAR" or "IGNORAR" — whether to place a bet.
/// <see cref="Seleccion"/>: an exact team name from the match (e.g.
/// "Real Madrid") or the literal "Empate".
/// <see cref="Confianza"/>: 0-100 confidence percentage. <see cref="Razonamiento"/>: rationale.
/// Kept <c>internal</c> because it is a low-level inference artifact;
/// controllers should map it to a public DTO when exposing it.
/// </summary>
public record PrediccionResult(string Decision, string Seleccion, int Confianza, string Razonamiento);

/// <summary>
/// A self-criticism rule produced by Ollama when a prediction was wrong.
/// </summary>
public record ReflectionResult(string Regla);
