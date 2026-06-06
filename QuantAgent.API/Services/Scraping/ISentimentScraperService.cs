namespace QuantAgent.API.Services.Scraping;

/// <summary>
/// Extracts news headlines from sports journalism sites
/// (Marca, AS, local Santa Cruz / Bolivia media) using Playwright.
/// <para>
/// This is deliberately lightweight — only headlines are extracted.
/// Full article text is NOT fetched. Each headline is a single sentence
/// passed to the sentiment analysis layer for scoring.
/// </para>
/// </summary>
public interface ISentimentScraperService
{
    /// <summary>
    /// Returns the latest N headlines mentioning <paramref name="teamName"/>,
    /// or global football headlines when teamName is empty.
    /// </summary>
    Task<List<string>> GetHeadlinesAsync(
        string? teamName = null,
        int maxResults = 10,
        CancellationToken ct = default);
}
