using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QuantAgent.API.Services.Scraping;

/// <summary>
/// Lightweight Playwright-based scraper that extracts match-relevant
/// headlines from Marca and AS (primary)  and feeds them to the
/// sentiment analysis pipeline.
/// <para>
/// Registered as <c>Scoped</c> because Playwright instances are
/// single-use. A new browser context is created per scrape cycle.
/// In Docker, Chromium is installed during the image build via
/// <c>playwright install chromium</c>.
/// </para>
/// </summary>
internal class SentimentScraperService : ISentimentScraperService
{
    private readonly ILogger<SentimentScraperService> _logger;

    /// <summary>
    /// Each source: (Url, HeadlineCssSelector, Name).
    /// The CSS selector targets article <c>&lt;h2&gt;</c> or
    /// <c>&lt;h3&gt;</c> elements that contain headlines.
    /// </summary>
    private static readonly NewsSource[] Sources =
    [
        new("https://www.marca.com/futbol.html", "h2", "Marca"),
        new("https://as.com/futbol/", "h2", "AS"),
        new("https://www.sport.es/es/", "h2", "Sport"),
    ];

    private static readonly string[] TeamNameStopwords =
        ["futbol", "noticias", "última hora", "resumen", "directo"];

    public SentimentScraperService(ILogger<SentimentScraperService> logger)
    {
        _logger = logger;
    }

    public async Task<List<string>> GetHeadlinesAsync(
        string? teamName = null,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        var headlines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);

        foreach (var source in Sources)
        {
            if (ct.IsCancellationRequested) break;
            if (headlines.Count >= maxResults) break;

            try
            {
                await page.GotoAsync(source.Url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 20_000,
                });

                var elements = await page.QuerySelectorAllAsync(source.CssSelector);
                foreach (var el in elements)
                {
                    if (headlines.Count >= maxResults) break;
                    var text = (await el.TextContentAsync())?.Trim();
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 15) continue;
                    if (seen.Contains(text)) continue;
                    seen.Add(text);

                    // Normalize whitespace
                    text = Regex.Replace(text, @"\s+", " ");

                    // If filtering by team, skip headlines that don't mention the team
                    if (!string.IsNullOrWhiteSpace(teamName) &&
                        !text.Contains(teamName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    headlines.Add(text);
                    _logger.LogDebug(
                        "Sentiment scraper [{Source}]: {Headline}",
                        source.Name, text);
                }

                _logger.LogInformation(
                    "Sentiment scraper [{Source}]: {Count} headlines extracted",
                    source.Name, Math.Min(elements.Count, maxResults - headlines.Count));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Sentiment scraper [{Source}]: failed to scrape {Url}",
                    source.Name, source.Url);
            }
        }

        await browser.CloseAsync();
        return headlines;
    }

    private sealed record NewsSource(string Url, string CssSelector, string Name);
}
