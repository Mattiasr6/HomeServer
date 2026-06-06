using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantAgent.API.Models;
using QuantAgent.API.Services.Scraping;

namespace QuantAgent.API.Services.Inference;

/// <summary>
/// Quantitative inference service backed by a local Ollama instance
/// running the <c>llama3.2</c> model. Builds a deterministic prompt
/// from the match + learned rules + live team statistics, posts to
/// <c>/api/generate</c>, and parses the JSON response into a
/// <see cref="PrediccionResult"/>.
/// </summary>
internal class OllamaInferenceService : IOllamaInferenceService
{
    private const string ModelName = "llama3.2";

    private readonly OllamaApiClient _ollama;
    private readonly ILogger<OllamaInferenceService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public OllamaInferenceService(OllamaApiClient ollama, ILogger<OllamaInferenceService> logger)
    {
        _ollama = ollama;
        _logger = logger;
    }

    public async Task<PrediccionResult> AnalyzeMatchAsync(
        Partido partido,
        List<ReglaAprendida> reglasEquipo,
        TeamStatsDto? localStats,
        TeamStatsDto? visitanteStats,
        decimal cuotaLocal,
        decimal cuotaEmpate,
        decimal cuotaVisita,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(partido, reglasEquipo, localStats, visitanteStats, cuotaLocal, cuotaEmpate, cuotaVisita);
        var request = new OllamaGenerateRequest(ModelName, prompt, false, "json");

        using var httpResponse = await _ollama.HttpClient
            .PostAsJsonAsync("api/generate", request, cancellationToken)
            .ConfigureAwait(false);

        httpResponse.EnsureSuccessStatusCode();

        // Outer envelope: { "model": "...", "response": "<inner json string>", "done": true, ... }
        var envelope = await httpResponse.Content
            .ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Ollama returned an empty body.");

        if (string.IsNullOrWhiteSpace(envelope.Response))
        {
            throw new InvalidOperationException("Ollama returned an empty 'response' field.");
        }

        // Inner JSON: the model has been instructed to produce strict JSON matching PrediccionResult.
        var result = JsonSerializer.Deserialize<PrediccionResult>(envelope.Response, JsonOptions)
            ?? throw new InvalidOperationException(
                "Ollama 'response' field could not be deserialized to PrediccionResult.");

        _logger.LogInformation(
            "Ollama inference for {Local} vs {Visitante}: {Decision} ({Confianza}%)",
            partido.EquipoLocal, partido.EquipoVisitante, result.Decision, result.Confianza);

        return result;
    }

    public async Task<ReflectionResult> GenerateReflectionAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest(ModelName, prompt, false, "json");

        using var httpResponse = await _ollama.HttpClient
            .PostAsJsonAsync("api/generate", request, cancellationToken)
            .ConfigureAwait(false);

        httpResponse.EnsureSuccessStatusCode();

        var envelope = await httpResponse.Content
            .ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Ollama returned an empty body.");

        if (string.IsNullOrWhiteSpace(envelope.Response))
        {
            throw new InvalidOperationException("Ollama returned an empty 'response' field.");
        }

        var result = JsonSerializer.Deserialize<ReflectionResult>(envelope.Response, JsonOptions)
            ?? throw new InvalidOperationException(
                "Ollama 'response' field could not be deserialized to ReflectionResult.");

        _logger.LogInformation(
            "Ollama reflection generated rule: {Rule}", result.Regla);

        return result;
    }

    // ---------- prompt construction -------------------------------------------

    private static string BuildPrompt(
        Partido partido,
        IReadOnlyList<ReglaAprendida> reglasEquipo,
        TeamStatsDto? localStats,
        TeamStatsDto? visitanteStats,
        decimal cuotaLocal,
        decimal cuotaEmpate,
        decimal cuotaVisita)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Eres un Analista Cuantitativo de Fútbol.");
        sb.AppendLine();
        sb.AppendLine("Analiza el siguiente partido y emite una predicción cuantitativa en formato JSON ESTRICTO.");
        sb.AppendLine("NO escribas texto fuera del JSON. NO uses bloques de código markdown.");
        sb.AppendLine();
        sb.AppendLine("== DATOS DEL PARTIDO ==");
        sb.AppendLine($"- Local: {partido.EquipoLocal}");
        sb.AppendLine($"- Visitante: {partido.EquipoVisitante}");
        sb.AppendLine($"- Fecha de inicio (UTC): {partido.FechaInicio:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("== ESTADÍSTICAS EN TIEMPO REAL (SoccerStats.com) ==");
        sb.Append(FormatTeamStats("LOCAL", partido.EquipoLocal, localStats));
        sb.Append(FormatTeamStats("VISITANTE", partido.EquipoVisitante, visitanteStats));
        sb.AppendLine();

        sb.AppendLine("== REGLAS APRENDIDAS PARA ESTOS EQUIPOS ==");
        if (reglasEquipo.Count == 0)
        {
            sb.AppendLine("  (ninguna regla aprendida todavía)");
        }
        else
        {
            foreach (var r in reglasEquipo)
            {
                sb.AppendLine($"  - [{r.Equipo}] (peso {r.Peso}) {r.Contexto}: {r.Regla}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("== MERCADO DE APUESTAS (BET365) ==");
        if (cuotaLocal > 0m && cuotaEmpate > 0m && cuotaVisita > 0m)
        {
            sb.AppendLine($"  - Cuota Local ({partido.EquipoLocal}): {cuotaLocal:N2}");
            sb.AppendLine($"  - Cuota Empate: {cuotaEmpate:N2}");
            sb.AppendLine($"  - Cuota Visitante ({partido.EquipoVisitante}): {cuotaVisita:N2}");
            sb.AppendLine();
            sb.AppendLine("El mercado financiero (casas de apuestas) valora este partido con las cuotas de arriba.");
            sb.AppendLine("Compara la probabilidad implícita de estas cuotas (1 / cuota) con tus propios");
            sb.AppendLine("cálculos basados en las estadísticas y reglas aprendidas.");
            sb.AppendLine("Si tu confianza supera la probabilidad implícita de la casa de apuestas, HAY VALUE BET.");
        }
        else
        {
            sb.AppendLine("  - (cuotas de mercado no disponibles aún)");
        }
        sb.AppendLine();

        // Note: literal '{' / '}' braces are placed in non-interpolated string
        // segments so the C# string interpolation does not trip on them.
        sb.Append("== FORMATO DE SALIDA REQUERIDO (JSON ESTRICTO) ==\n" +
                  "{\n" +
                  "  \"decision\": \"<APOSTAR o IGNORAR>\",\n" +
                  "  \"seleccion\": \"<EXACTAMENTE el nombre del equipo local, el visitante, o 'Empate'>\",\n" +
                  "  \"confianza\": <entero entre 0 y 100>,\n" +
                  "  \"razonamiento\": \"<texto breve justificando la decisión>\"\n" +
                  "}");

        return sb.ToString();
    }

    private static string FormatTeamStats(string label, string teamName, TeamStatsDto? stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{label}] {teamName}:");
        if (stats == null)
        {
            sb.AppendLine("  - (no se pudieron obtener estadísticas en tiempo real)");
        }
        else
        {
            sb.AppendLine($"  - Posición en la liga: {stats.Posicion}");
            sb.AppendLine($"  - Puntos: {stats.Puntos}");
            sb.AppendLine($"  - Goles a favor: {stats.GolesFavor}");
            sb.AppendLine($"  - Goles en contra: {stats.GolesContra}");
            sb.AppendLine($"  - % Over 2.5 (temporada): {stats.Over25}");
            sb.AppendLine($"  - Córners promedio en casa: {stats.CornersLocal:0.00}");
            sb.AppendLine($"  - Córners promedio fuera: {stats.CornersVisitante:0.00}");
        }
        return sb.ToString();
    }

    // ---------- Ollama wire types ---------------------------------------------

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] string Format);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string Response);
}
