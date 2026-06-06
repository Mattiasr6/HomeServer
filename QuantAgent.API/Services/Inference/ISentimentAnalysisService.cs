namespace QuantAgent.API.Services.Inference;

/// <summary>
/// Analyses news headlines (scraped by <c>ISentimentScraperService</c>)
/// via Ollama and produces a sentiment score between -1 (very negative)
/// and +1 (very positive) per team.
/// <para>
/// When a headline indicates bad news for a team (e.g. key player
/// injured), the score is negative and <see cref="ValueBetDetectionJob"/>
/// can use it to penalise the stake for that team's side.
/// </para>
/// </summary>
public interface ISentimentAnalysisService
{
    /// <summary>
    /// Scores a set of headlines for sentiment relevant to
    /// <paramref name="teamName"/>. Returns a score in [-1, 1]
    /// where negative means bad news for the team.
    /// When no headlines are provided, returns 0 (neutral).
    /// </summary>
    Task<SentimentScoreDto> ScoreTeamSentimentAsync(
        string teamName,
        List<string> headlines,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a team-level sentiment analysis.
/// <see cref="Score"/> ranges from -1 (very negative) to +1 (very positive).
/// <see cref="Confianza"/> indicates how certain the model is (0-100).
/// </summary>
public record SentimentScoreDto(
    string TeamName,
    double Score,
    int Confianza,
    string Resumen);
