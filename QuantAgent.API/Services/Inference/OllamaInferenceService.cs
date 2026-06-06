using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;
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
    private readonly ITelemetryService _telemetry;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public OllamaInferenceService(
        OllamaApiClient ollama,
        ITelemetryService telemetry,
        ILogger<OllamaInferenceService> logger)
    {
        _ollama = ollama;
        _telemetry = telemetry;
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
        // Default market prompt — Match Winner (keeps backward compat)
        return await AnalyzeMarketAsync(
            partido, reglasEquipo, localStats, visitanteStats,
            TipoMercado.Ganador,
            cuotaLocal, cuotaEmpate, cuotaVisita,
            0m, 0m, 0m, 0m,
            cancellationToken);
    }

    public async Task<PrediccionResult> AnalyzeMarketAsync(
        Partido partido,
        List<ReglaAprendida> reglasEquipo,
        TeamStatsDto? localStats,
        TeamStatsDto? visitanteStats,
        TipoMercado mercado,
        decimal cuotaLocal,
        decimal cuotaEmpate,
        decimal cuotaVisita,
        decimal cornersOverOdds,
        decimal cornersUnderOdds,
        decimal goalsOverOdds,
        decimal goalsUnderOdds,
        CancellationToken cancellationToken = default)
    {
        // First attempt
        var prompt = BuildPrompt(partido, reglasEquipo, localStats, visitanteStats,
            mercado, cuotaLocal, cuotaEmpate, cuotaVisita,
            cornersOverOdds, cornersUnderOdds, goalsOverOdds, goalsUnderOdds, retry: false);

        var result = await CallAndParseAsync(prompt, cancellationToken);
        await _telemetry.BroadcastLogAsync(string.Format(
            "Ollama infiriendo para {0} vs {1} [{2}]...",
            partido.EquipoLocal, partido.EquipoVisitante, mercado), "AI");

        // Validate - if malformed, retry once with stricter prompt
        if (!IsValidResult(result, mercado))
        {
            _logger.LogWarning(
                "Invalid inference for {Market}: seleccion='{Sel}' confianza={Conf} - retrying with stricter prompt",
                mercado, result.Seleccion, result.Confianza);

            var retryPrompt = BuildPrompt(partido, reglasEquipo, localStats, visitanteStats,
                mercado, cuotaLocal, cuotaEmpate, cuotaVisita,
                cornersOverOdds, cornersUnderOdds, goalsOverOdds, goalsUnderOdds, retry: true);

            try
            {
                result = await CallAndParseAsync(retryPrompt, cancellationToken);
            }
            catch (Exception exRetry)
            {
                _logger.LogError(exRetry, "Retry also failed for {Market}", mercado);
                throw;
            }

            // If still invalid after retry, force safe defaults
            if (!IsValidResult(result, mercado))
            {
                _logger.LogWarning(
                    "Retry still produced invalid result for {Market} - applying safe defaults", mercado);
                result = result with
                {
                    Decision = "IGNORAR",
                    Confianza = Math.Clamp(result.Confianza, 0, 100),
                    Seleccion = string.IsNullOrWhiteSpace(result.Seleccion)
                        ? GetDefaultSeleccion(mercado)
                        : result.Seleccion
                };
            }
            await _telemetry.BroadcastLogAsync(string.Format(
                "Retry necesario para {0} vs {1} [{2}] - resultado invalido",
                partido.EquipoLocal, partido.EquipoVisitante, mercado), "ERROR");
        }

        // Clamp confianza to [0, 100] as final safety net
        if (result.Confianza < 0 || result.Confianza > 100)
        {
            result = result with { Confianza = Math.Clamp(result.Confianza, 0, 100) };
        }

        _logger.LogInformation(
            "Ollama inference for {Local} vs {Visitante} [{Mercado}]: {Decision} ({Confianza}%)",
            partido.EquipoLocal, partido.EquipoVisitante, mercado, result.Decision, result.Confianza);

        await _telemetry.BroadcastLogAsync(string.Format(
            "Inferencia completada: {0} vs {1} [{2}] -> {3} ({4}%)",
            partido.EquipoLocal, partido.EquipoVisitante,
            mercado, result.Decision, result.Confianza), "INFO");
        return result;
    }

    public async Task<ReflectionResult> GenerateReflectionAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        return await CallAndParseReflectionAsync(prompt, cancellationToken);
    }

    // ---------- core HTTP + parse helpers -----------------------------------

    private async Task<PrediccionResult> CallAndParseAsync(string prompt, CancellationToken ct)
    {
        var envelope = await CallOllamaRawAsync(prompt, ct);
        return JsonSerializer.Deserialize<PrediccionResult>(envelope.Response, JsonOptions)
            ?? throw new InvalidOperationException(
                "Ollama 'response' field could not be deserialized to PrediccionResult.");
    }

    private async Task<ReflectionResult> CallAndParseReflectionAsync(string prompt, CancellationToken ct)
    {
        var envelope = await CallOllamaRawAsync(prompt, ct);
        return JsonSerializer.Deserialize<ReflectionResult>(envelope.Response, JsonOptions)
            ?? throw new InvalidOperationException(
                "Ollama 'response' field could not be deserialized to ReflectionResult.");
    }

    private async Task<OllamaGenerateResponse> CallOllamaRawAsync(string prompt, CancellationToken ct)
    {
        var request = new OllamaGenerateRequest(ModelName, prompt, false, "json");
        using var httpResponse = await _ollama.HttpClient
            .PostAsJsonAsync("api/generate", request, ct)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var envelope = await httpResponse.Content
            .ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Ollama returned an empty body.");

        if (string.IsNullOrWhiteSpace(envelope.Response))
        {
            throw new InvalidOperationException("Ollama returned an empty 'response' field.");
        }

        return envelope;
    }

    // ---------- result validation -------------------------------------------

    private static bool IsValidResult(PrediccionResult result, TipoMercado mercado)
    {
        if (result.Confianza < 0 || result.Confianza > 100) return false;
        if (string.IsNullOrWhiteSpace(result.Seleccion)) return false;
        if (result.Decision != "APOSTAR" && result.Decision != "IGNORAR") return false;

        return mercado switch
        {
            TipoMercado.Corners => result.Seleccion is "Over 9.5" or "Under 9.5",
            TipoMercado.Goles => result.Seleccion is "Over 2.5" or "Under 2.5",
            _ => true // Ganador: any team name or Empate is valid
        };
    }

    private static string GetDefaultSeleccion(TipoMercado mercado) => mercado switch
    {
        TipoMercado.Corners => "Under 9.5",
        TipoMercado.Goles => "Under 2.5",
        _ => "Empate"
    };

    // ---------- prompt construction -------------------------------------------
    private static string BuildPrompt(
        Partido partido,
        IReadOnlyList<ReglaAprendida> reglasEquipo,
        TeamStatsDto? localStats,
        TeamStatsDto? visitanteStats,
        TipoMercado mercado,
        decimal cuotaLocal,
        decimal cuotaEmpate,
        decimal cuotaVisita,
        decimal cornersOverOdds,
        decimal cornersUnderOdds,
        decimal goalsOverOdds,
        decimal goalsUnderOdds,
        bool retry = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Eres un Analista Cuantitativo de Futbol especializado en mercados secundarios.");
        sb.AppendLine();
        sb.AppendLine("Analiza el siguiente partido y emite UNA prediccion cuantitativa en formato JSON ESTRICTO.");
        sb.AppendLine("NO escribas texto fuera del JSON. NO uses bloques de codigo markdown.");
        sb.AppendLine("NO devuelvas campos vacios o nulos.");
        sb.AppendLine();

        if (retry)
        {
            sb.AppendLine("== IMPORTANTE: tu respuesta anterior tenia errores de formato ==");
            sb.AppendLine("Asegurate de que 'seleccion' NO este vacio.");
            sb.AppendLine("Asegurate de que 'confianza' sea un NUMERO ENTERO entre 0 y 100 (no decimal).");
            sb.AppendLine("Usa EXACTAMENTE uno de los valores permitidos para 'seleccion'.");
            sb.AppendLine();
        }

        sb.AppendLine("== DATOS DEL PARTIDO ==");
        sb.AppendLine($"- Local: {partido.EquipoLocal}");
        sb.AppendLine($"- Visitante: {partido.EquipoVisitante}");
        sb.AppendLine($"- Fecha de inicio (UTC): {partido.FechaInicio:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("== ESTADISTICAS EN TIEMPO REAL (SoccerStats.com) ==");
        sb.Append(FormatTeamStats("LOCAL", partido.EquipoLocal, localStats));
        sb.Append(FormatTeamStats("VISITANTE", partido.EquipoVisitante, visitanteStats));
        sb.AppendLine();

        sb.AppendLine("== REGLAS APRENDIDAS PARA ESTOS EQUIPOS ==");
        if (reglasEquipo.Count == 0)
        {
            sb.AppendLine("  (ninguna regla aprendida todavia)");
        }
        else
        {
            foreach (var r in reglasEquipo)
            {
                sb.AppendLine($"  - [{r.Equipo}] (peso {r.Peso}) {r.Contexto}: {r.Regla}");
            }
        }
        sb.AppendLine();

        var marketLabel = mercado switch
        {
            TipoMercado.Corners => "CORNERS",
            TipoMercado.Goles => "GOLES",
            _ => "GANADOR"
        };
        sb.AppendLine($"== MERCADO OBJETIVO: {marketLabel} ==");

        switch (mercado)
        {
            case TipoMercado.Ganador:
                sb.AppendLine("Analiza que equipo ganara el partido.");
                sb.AppendLine("Considera estadisticas ofensivas/defensivas, reglas aprendidas y tendencias.");
                if (cuotaLocal > 0m && cuotaEmpate > 0m && cuotaVisita > 0m)
                {
                    sb.AppendLine();
                    sb.AppendLine("== CUOTAS BET365 ==");
                    sb.AppendLine($"  - Local ({partido.EquipoLocal}): {cuotaLocal:N2}");
                    sb.AppendLine($"  - Empate: {cuotaEmpate:N2}");
                    sb.AppendLine($"  - Visitante ({partido.EquipoVisitante}): {cuotaVisita:N2}");
                    sb.AppendLine();
                    sb.AppendLine("Compara la probabilidad implicita (1/cuota) con tus calculos.");
                    sb.AppendLine("Si tu confianza supera la probabilidad implicita, HAY VALUE BET.");
                }
                break;

            case TipoMercado.Corners:
                sb.AppendLine("Analiza el total de corners del partido, no el ganador.");
                sb.AppendLine("Considera el promedio de corners generados/recibidos por cada equipo.");
                sb.AppendLine("Umbral de analisis: 9.5 corners totales.");
                if (cornersOverOdds > 0m && cornersUnderOdds > 0m)
                {
                    sb.AppendLine();
                    sb.AppendLine("== CUOTAS BET365 (CORNERS O/U 9.5) ==");
                    sb.AppendLine($"  - Over 9.5: {cornersOverOdds:N2}");
                    sb.AppendLine($"  - Under 9.5: {cornersUnderOdds:N2}");
                    sb.AppendLine();
                    var overImplied = 1m / cornersOverOdds;
                    var underImplied = 1m / cornersUnderOdds;
                    sb.AppendLine($"Probabilidad implicita: Over={overImplied:P1} Under={underImplied:P1}");
                }
                break;

            case TipoMercado.Goles:
                sb.AppendLine("Analiza el total de goles del partido, no el ganador.");
                sb.AppendLine("Considera el historial ofensivo/defensivo y el % Over 2.5 de cada equipo.");
                sb.AppendLine("Umbral de analisis: 2.5 goles totales.");
                if (goalsOverOdds > 0m && goalsUnderOdds > 0m)
                {
                    sb.AppendLine();
                    sb.AppendLine("== CUOTAS BET365 (GOLES O/U 2.5) ==");
                    sb.AppendLine($"  - Over 2.5: {goalsOverOdds:N2}");
                    sb.AppendLine($"  - Under 2.5: {goalsUnderOdds:N2}");
                    sb.AppendLine();
                    var overImplied = 1m / goalsOverOdds;
                    var underImplied = 1m / goalsUnderOdds;
                    sb.AppendLine($"Probabilidad implicita: Over={overImplied:P1} Under={underImplied:P1}");
                }
                break;
        }

        sb.AppendLine();
        sb.AppendLine("== FORMATO DE SALIDA REQUERIDO (JSON ESTRICTO) ==");
        sb.AppendLine("{");
        sb.AppendLine("  \"decision\": \"<APOSTAR o IGNORAR>\",");
        sb.AppendLine("  \"seleccion\": \"<VER LISTA DE VALORES PERMITIDOS ABAJO>\",");
        sb.AppendLine("  \"confianza\": <entero entre 0 y 100>,");
        sb.AppendLine("  \"razonamiento\": \"<texto breve justificando la decision>\"");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("VALORES PERMITIDOS para 'seleccion' (usa EXACTAMENTE uno, sin texto adicional):");
        sb.AppendLine("  - Para GANADOR: el nombre exacto del equipo local, el nombre exacto del equipo visitante, o \"Empate\"");
        sb.AppendLine("  - Para CORNERS: \"Over 9.5\" o \"Under 9.5\"");
        sb.AppendLine("  - Para GOLES: \"Over 2.5\" o \"Under 2.5\"");
        sb.AppendLine();
        sb.AppendLine("EJEMPLO CORRECTO para CORNERS:");
        sb.AppendLine("{");
        sb.AppendLine("  \"decision\": \"IGNORAR\",");
        sb.AppendLine("  \"seleccion\": \"Under 9.5\",");
        sb.AppendLine("  \"confianza\": 40,");
        sb.AppendLine("  \"razonamiento\": \"Promedio de corners bajo, improbable que supere 9.5.\"");
        sb.AppendLine("}");

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
