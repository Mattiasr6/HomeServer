using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace QuantAgent.API.Services;

/// <summary>
/// HTTP client for TheOddsAPI v4 (<see href="https://the-odds-api.com"/>).
/// Fetches live Match Winner odds from multiple bookmakers for a given
/// fixture, using team names as the query key.
/// <para>
/// Sport key mapping: our league IDs are mapped to TheOddsAPI soccer
/// keys in <c>appsettings.json</c> under <c>OddsApi:SportMapping</c>.
/// </para>
/// <para>
/// Registered as <c>Scoped</c> because it carries per-request state
/// (the HttpClient is managed by the IHttpClientFactory pool).
/// </para>
/// </summary>
public class AlternativeOddsService : IAlternativeOddsService
{
    private const string BaseUrl = "https://api.the-odds-api.com/v4/";

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AlternativeOddsService> _logger;

    public AlternativeOddsService(
        HttpClient http,
        IConfiguration config,
        ILogger<AlternativeOddsService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<List<BookmakerOddsDto>> GetMatchWinnerOddsAsync(
        string homeTeam, string awayTeam, int sportId, CancellationToken ct = default)
    {
        var apiKey = _config["OddsApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OddsApi:ApiKey not configured — skipping alternative odds fetch.");
            return [];
        }

        var sportKey = ResolveSportKey(sportId);
        if (sportKey is null)
        {
            _logger.LogDebug(
                "No OddsApi sport mapping for {Home} vs {Away} — skipping.", homeTeam, awayTeam);
            return [];
        }

        try
        {
            var url = $"sports/{sportKey}/odds/?apiKey={apiKey}&regions=eu&markets=h2h&oddsFormat=decimal";
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var fixtures = await response.Content
                .ReadFromJsonAsync<List<TheOddsFixture>>(
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct)
                ?? [];

            // Find the fixture matching both team names (case-insensitive)
            var match = fixtures.FirstOrDefault(f =>
                f.HomeTeam.Equals(homeTeam, StringComparison.OrdinalIgnoreCase) &&
                f.AwayTeam.Equals(awayTeam, StringComparison.OrdinalIgnoreCase));

            if (match?.Bookmakers is null || match.Bookmakers.Count == 0)
            {
                _logger.LogInformation(
                    "TheOddsAPI: no match found for {Home} vs {Away}", homeTeam, awayTeam);
                return [];
            }

            var results = new List<BookmakerOddsDto>(match.Bookmakers.Count);
            foreach (var bm in match.Bookmakers)
            {
                var h2h = bm.Markets?.FirstOrDefault(m =>
                    m.Key.Equals("h2h", StringComparison.OrdinalIgnoreCase));
                if (h2h?.Outcomes is null || h2h.Outcomes.Count < 3) continue;

                var homeOdd = h2h.Outcomes.FirstOrDefault(o =>
                    o.Name.Equals(homeTeam, StringComparison.OrdinalIgnoreCase))?.Price ?? 0m;
                var awayOdd = h2h.Outcomes.FirstOrDefault(o =>
                    o.Name.Equals(awayTeam, StringComparison.OrdinalIgnoreCase))?.Price ?? 0m;
                var drawOdd = h2h.Outcomes.FirstOrDefault(o =>
                    !o.Name.Equals(homeTeam, StringComparison.OrdinalIgnoreCase) &&
                    !o.Name.Equals(awayTeam, StringComparison.OrdinalIgnoreCase))?.Price ?? 0m;

                if (homeOdd > 0 && awayOdd > 0 && drawOdd > 0)
                {
                    results.Add(new BookmakerOddsDto(bm.Title, homeOdd, drawOdd, awayOdd));
                }
            }

            _logger.LogInformation(
                "TheOddsAPI: {Count} bookmaker odds for {Home} vs {Away}",
                results.Count, homeTeam, awayTeam);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TheOddsAPI request failed for {Home} vs {Away}", homeTeam, awayTeam);
            return [];
        }
    }

    /// <summary>
    /// Maps one of the participant team names to a TheOddsAPI sport key
    /// using the configured <c>OddsApi:SportMapping</c> dictionary.
    /// Falls back to <c>soccer_epl</c> on no match (conservative default).
    /// </summary>
    private string? ResolveSportKey(int sportId)
    {
        var mapping = _config.GetSection("OddsApi:SportMapping")
            .Get<Dictionary<string, string>>();

        if (mapping is null || mapping.Count == 0)
            return null;

        var idStr = sportId.ToString();
        return mapping.TryGetValue(idStr, out var key) ? key : null;
    }

    // ---------- TheOddsAPI wire types (System.Text.Json) -----------------------

    private sealed record TheOddsFixture(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("home_team")] string HomeTeam,
        [property: JsonPropertyName("away_team")] string AwayTeam,
        [property: JsonPropertyName("bookmakers")] List<TheOddsBookmaker>? Bookmakers);

    private sealed record TheOddsBookmaker(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("markets")] List<TheOddsMarket>? Markets);

    private sealed record TheOddsMarket(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("outcomes")] List<TheOddsOutcome>? Outcomes);

    private sealed record TheOddsOutcome(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("price")] decimal Price);
}
