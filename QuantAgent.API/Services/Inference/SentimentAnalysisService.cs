using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuantAgent.API.Services.Inference;

/// <summary>
/// Calls Ollama (llama3.2) with a structured prompt to score news
/// headlines for sentiment toward a specific team.
/// <para>
/// The model returns a JSON object with <c>score</c> (-1 to 1),
/// <c>confianza</c> (0-100), and <c>resumen</c> (brief rationale).
/// </para>
/// <para>
/// Registered as <c>Scoped</c> to share the Ollama HTTP client's
/// lifetime with <see cref="OllamaInferenceService"/>.
/// </para>
/// </summary>
internal class SentimentAnalysisService : ISentimentAnalysisService
{
    private const string ModelName = "llama3.2";

    private readonly OllamaApiClient _ollama;
    private readonly ILogger<SentimentAnalysisService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SentimentAnalysisService(
        OllamaApiClient ollama,
        ILogger<SentimentAnalysisService> logger)
    {
        _ollama = ollama;
        _logger = logger;
    }

    public async Task<SentimentScoreDto> ScoreTeamSentimentAsync(
        string teamName,
        List<string> headlines,
        CancellationToken ct = default)
    {
        if (headlines.Count == 0)
        {
            return new SentimentScoreDto(teamName, 0.0, 0, "Sin titulares disponibles.");
        }

        var prompt = BuildSentimentPrompt(teamName, headlines);

        try
        {
            var request = new OllamaRequest(ModelName, prompt, false, "json");
            using var httpResponse = await _ollama.HttpClient
                .PostAsJsonAsync("api/generate", request, ct)
                .ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            var envelope = await httpResponse.Content
                .ReadFromJsonAsync<OllamaResponse>(JsonOptions, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Ollama returned empty body for sentiment analysis.");

            if (string.IsNullOrWhiteSpace(envelope.Response))
                throw new InvalidOperationException("Ollama returned empty 'response' field for sentiment.");

            var parsed = JsonSerializer.Deserialize<SentimentRaw>(envelope.Response, JsonOptions)
                ?? new SentimentRaw(0.0, 0, "No se pudo interpretar la respuesta.");

            // Clamp score to [-1, 1] and confianza to [0, 100]
            var score = Math.Clamp(parsed.Score, -1.0, 1.0);
            var confianza = Math.Clamp(parsed.Confianza, 0, 100);

            _logger.LogInformation(
                "Sentiment for '{Team}': score={Score:F2} confianza={Conf}% — {Resumen}",
                teamName, score, confianza, parsed.Resumen);

            return new SentimentScoreDto(teamName, score, confianza, parsed.Resumen);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sentiment analysis failed for '{Team}'", teamName);
            return new SentimentScoreDto(teamName, 0.0, 0, $"Error: {ex.Message}");
        }
    }

    private static string BuildSentimentPrompt(string teamName, List<string> headlines)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Eres un analista de sentimiento deportivo. Debes evaluar si las siguientes");
        sb.AppendLine("noticias son positivas o negativas para el equipo indicado.");
        sb.AppendLine();
        sb.AppendLine($"Equipo objetivo: {teamName}");
        sb.AppendLine();
        sb.AppendLine("Titulares:");
        foreach (var h in headlines)
        {
            sb.AppendLine($"  - {h}");
        }
        sb.AppendLine();
        sb.AppendLine("Devuelve SOLAMENTE un objeto JSON con estos campos (sin markdown, sin texto adicional):");
        sb.AppendLine("{");
        sb.AppendLine("  \"score\": <numero decimal entre -1.0 y 1.0, donde -1 es muy negativo y +1 muy positivo>,");
        sb.AppendLine("  \"confianza\": <entero 0-100 indicando que tan seguro estas de tu evaluacion>,");
        sb.AppendLine("  \"resumen\": \"<breve explicacion de por que el sentimiento es ese>\"");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ---------- Ollama wire types --------------------------------------------

    private sealed record OllamaRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] string Format);

    private sealed record OllamaResponse(
        [property: JsonPropertyName("response")] string Response);

    private sealed record SentimentRaw(
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("confianza")] int Confianza,
        [property: JsonPropertyName("resumen")] string Resumen);
}
