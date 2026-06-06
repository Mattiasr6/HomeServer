using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Models;

namespace QuantAgent.API.Services;

/// <summary>
/// Typed HTTP client for the API-Football v3 service
/// (<see href="https://v3.football.api-sports.io/"/>).
/// The API key is resolved dynamically via
/// <see cref="IKeyRotationService"/> so the system can rotate
/// between multiple keys when 429 (Rate Limit) is received.
/// </summary>
public class FootballApiService
{
    private readonly HttpClient _httpClient;
    private readonly IKeyRotationService _keyRotation;
    private readonly ILogger<FootballApiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string Deporte = "football";

    public FootballApiService(
        HttpClient httpClient,
        IKeyRotationService keyRotation,
        ILogger<FootballApiService> logger)
    {
        _httpClient = httpClient;
        _keyRotation = keyRotation;
        _logger = logger;
    }

    /// <summary>
    /// Fetches the real result for a fixture from API-Football.
    /// Returns the home goals, away goals, and the match status.
    /// Throws if the fixture is not yet finished — caller should retry.
    /// </summary>
    public async Task<(int GolesLocal, int GolesVisitante, EstadoPartido Estado)> GetMatchResultAsync(int fixtureId)
    {
        using var response = await SendWithKeyRotationAsync(HttpMethod.Get, $"/fixtures?id={fixtureId}");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<FixtureApiResponse>(JsonOptions);

        var fixture = doc?.Response?.FirstOrDefault()
            ?? throw new InvalidOperationException($"Fixture {fixtureId} not found in API response.");

        var statusShort = fixture.Fixture.Status.Short;

        if (statusShort != "FT")
        {
            throw new InvalidOperationException(
                $"Fixture {fixtureId} has status '{statusShort}' — not finished yet. Retrying later.");
        }

        var homeGoals = fixture.Score.FullTime?.Home
            ?? throw new InvalidOperationException($"Fixture {fixtureId} is FT but has no full-time home score.");
        var awayGoals = fixture.Score.FullTime?.Away
            ?? throw new InvalidOperationException($"Fixture {fixtureId} is FT but has no full-time away score.");

        _logger.LogInformation(
            "Fixtures API for {FixtureId}: {Home}-{Away} (FT)",
            fixtureId, homeGoals, awayGoals);

        return (homeGoals, awayGoals, EstadoPartido.Finalizado);
    }

    /// <summary>
    /// Fetches all fixtures for a given date from API-Football, filtering
    /// to the specified league IDs. Returns a list of external DTOs ready
    /// for persistence by <see cref="Jobs.MatchIngestionJob"/>.
    /// </summary>
    public async Task<List<PartidoExternoDto>> GetDailyFixturesAsync(
        DateTime date, int[] leagueIds)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        using var response = await SendWithKeyRotationAsync(HttpMethod.Get, $"/fixtures?date={dateStr}");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<DailyFixturesResponse>(JsonOptions);

        var fixtures = doc?.Response ?? [];

        var filtered = fixtures
            .Where(f => leagueIds.Contains(f.League.Id))
            .Select(f => new PartidoExternoDto(
                FixtureId: f.Fixture.Id,
                EquipoLocal: f.Teams.Home.Name,
                EquipoVisitante: f.Teams.Away.Name,
                FechaInicio: DateTime.Parse(f.Fixture.Date, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                LeagueId: f.League.Id))
            .ToList();

        _logger.LogInformation(
            "Daily fixtures: {Total} total, {Filtered} in target leagues ({Leagues})",
            fixtures.Count, filtered.Count, string.Join(",", leagueIds));

        return filtered;
    }

    /// <summary>
    /// Fetches a single fixture by its API-Football ID.
    /// Throws if the fixture does not exist.
    /// </summary>
    public async Task<PartidoExternoDto> GetFixtureByIdAsync(int fixtureId)
    {
        using var response = await SendWithKeyRotationAsync(HttpMethod.Get, $"/fixtures?id={fixtureId}");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<DailyFixturesResponse>(JsonOptions);

        var item = doc?.Response?.FirstOrDefault()
            ?? throw new InvalidOperationException($"Fixture {fixtureId} not found in API response.");

        return new PartidoExternoDto(
            FixtureId: item.Fixture.Id,
            EquipoLocal: item.Teams.Home.Name,
            EquipoVisitante: item.Teams.Away.Name,
            FechaInicio: DateTime.Parse(item.Fixture.Date, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
            LeagueId: item.League.Id);
    }

    /// <summary>
    /// Fetches ALL fixtures for today from API-Football, without
    /// filtering by league. Used by the <c>/buscar</c> Telegram
    /// command so users can discover any fixture ID.
    /// </summary>
    public async Task<List<PartidoExternoDto>> GetAllFixturesTodayAsync()
    {
        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        using var response = await SendWithKeyRotationAsync(HttpMethod.Get, $"/fixtures?date={dateStr}");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<DailyFixturesResponse>(JsonOptions);

        var fixtures = doc?.Response ?? [];

        return fixtures.Select(f => new PartidoExternoDto(
            FixtureId: f.Fixture.Id,
            EquipoLocal: f.Teams.Home.Name,
            EquipoVisitante: f.Teams.Away.Name,
            FechaInicio: DateTime.Parse(f.Fixture.Date, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
            LeagueId: f.League.Id)).ToList();
    }

    /// <summary>
    /// Fetches Bet365 odds for a fixture from API-Football.
    /// Returns (0, 0, 0) if odds are not yet published or the
    /// Match Winner market is unavailable.
    /// </summary>
    public async Task<(decimal CuotaLocal, decimal CuotaEmpate, decimal CuotaVisita)> GetMatchOddsAsync(int fixtureId)
    {
        using var response = await SendWithKeyRotationAsync(HttpMethod.Get, $"/odds?fixture={fixtureId}&bookmaker=8");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("No odds found for fixture {FixtureId}", fixtureId);
            return (0m, 0m, 0m);
        }
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<OddsApiResponse>(JsonOptions);

        var fixtureOdds = doc?.Response?.FirstOrDefault();
        if (fixtureOdds is null)
        {
            _logger.LogInformation("No odds found for fixture {FixtureId}", fixtureId);
            return (0m, 0m, 0m);
        }

        var bet365 = fixtureOdds.Bookmakers?.FirstOrDefault(b => b.Id == 8);
        if (bet365 is null)
        {
            _logger.LogInformation("Bet365 odds not available for fixture {FixtureId}", fixtureId);
            return (0m, 0m, 0m);
        }

        var market = bet365.Bets?.FirstOrDefault(b =>
            b.Name.Equals("Match Winner", StringComparison.OrdinalIgnoreCase) ||
            b.Name.Equals("1x2", StringComparison.OrdinalIgnoreCase) ||
            b.Name.Equals("Match Result", StringComparison.OrdinalIgnoreCase));

        if (market?.Values is null || market.Values.Count < 3)
        {
            _logger.LogInformation("Match Winner market not found for fixture {FixtureId}", fixtureId);
            return (0m, 0m, 0m);
        }

        static decimal ParseOdd(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0m;
            return decimal.TryParse(raw,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val)
                ? val
                : 0m;
        }

        var homeOdds  = ParseOdd(market.Values.FirstOrDefault(v =>
            v.Value.Equals("Home", StringComparison.OrdinalIgnoreCase))?.Odd);
        var drawOdds  = ParseOdd(market.Values.FirstOrDefault(v =>
            v.Value.Equals("Draw", StringComparison.OrdinalIgnoreCase))?.Odd);
        var awayOdds  = ParseOdd(market.Values.FirstOrDefault(v =>
            v.Value.Equals("Away", StringComparison.OrdinalIgnoreCase))?.Odd);

        _logger.LogInformation(
            "Odds for fixture {FixtureId}: Home={Home} Draw={Draw} Away={Away}",
            fixtureId, homeOdds, drawOdds, awayOdds);

        return (homeOdds, drawOdds, awayOdds);
    }

    /// <summary>
    /// Fetches corners over/under odds from Bet365 for a given fixture.
    /// Returns (0, 0) if the market is not available.
    /// Typical bet name: "Corners O/U" or "Total Corners", threshold ~9.5.
    /// </summary>
    public async Task<(decimal OverOdds, decimal UnderOdds)> GetCornersOddsAsync(int fixtureId)
    {
        return await GetOverUnderOddsAsync(fixtureId, ["Total Corners", "Corners O/U", "Total Corner"]);
    }

    /// <summary>
    /// Fetches goals over/under odds at the 2.5 threshold from Bet365.
    /// Returns (0, 0) if the market is not available.
    /// </summary>
    public async Task<(decimal OverOdds, decimal UnderOdds)> GetGoalsOddsAsync(int fixtureId)
    {
        return await GetOverUnderOddsAsync(fixtureId, ["Over/Under"]);
    }

    /// <summary>
    /// Generic helper: searches the Bet365 odds response for a named market
    /// that contains "Over/Under" values and returns the first match.
    /// </summary>
    private async Task<(decimal OverOdds, decimal UnderOdds)> GetOverUnderOddsAsync(
        int fixtureId, string[] marketNames)
    {
        using var response = await SendWithKeyRotationAsync(HttpMethod.Get, $"/odds?fixture={fixtureId}&bookmaker=8");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (0m, 0m);
        }
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<OddsApiResponse>(JsonOptions);
        var fixtureOdds = doc?.Response?.FirstOrDefault();
        if (fixtureOdds is null) return (0m, 0m);

        var bet365 = fixtureOdds.Bookmakers?.FirstOrDefault(b => b.Id == 8);
        if (bet365 is null) return (0m, 0m);

        // Find the first matching market
        var market = bet365.Bets?.FirstOrDefault(b =>
            marketNames.Any(n =>
                b.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));

        if (market?.Values is null) return (0m, 0m);

        static decimal ParseOdd(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0m;
            return decimal.TryParse(raw,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val)
                ? val
                : 0m;
        }

        // Values come as entries like {"value": "Over 9.5", "odd": "1.80"}
        var overOdd  = ParseOdd(market.Values.FirstOrDefault(v =>
            v.Value.StartsWith("Over", StringComparison.OrdinalIgnoreCase))?.Odd);
        var underOdd = ParseOdd(market.Values.FirstOrDefault(v =>
            v.Value.StartsWith("Under", StringComparison.OrdinalIgnoreCase))?.Odd);

        _logger.LogInformation(
            "Over/Under odds for fixture {FixtureId} in '{Market}': Over={Over} Under={Under}",
            fixtureId, market.Name, overOdd, underOdd);

        return (overOdd, underOdd);
    }

    /// <summary>
    /// Core helper: resolves a valid API key via <see cref="IKeyRotationService"/>,
    /// builds a request with the dynamic <c>x-apisports-key</c> header, sends it,
    /// and handles 429 (Rate Limit) by marking the key as Limited and throwing.
    /// On success, records the usage.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithKeyRotationAsync(
        HttpMethod method, string path)
    {
        var key = await _keyRotation.GetValidKeyAsync(Deporte);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Remove("x-apisports-key");
        request.Headers.Add("x-apisports-key", key.Key);

        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await _keyRotation.MarkAsLimitedAsync(key.Id);
            _logger.LogWarning(
                "[KEYROTATION] Key {KeyId} rate-limited (429) on {Method} {Path}. " +
                "Rotation triggered — next call will use a different key.",
                key.Id, method, path);
            throw new HttpRequestException(
                $"API-Football rate limit exceeded on {method} {path}. Key {key.Id} marked as Limited.",
                null, HttpStatusCode.TooManyRequests);
        }

        if (response.IsSuccessStatusCode)
        {
            await _keyRotation.RecordSuccessAsync(key.Id);
        }
        else
        {
            await _keyRotation.RecordFailureAsync(key.Id);
        }

        return response;
    }

    // ---------- API-Football wire types ----------------------------------------

    private sealed record FixtureApiResponse(
        [property: JsonPropertyName("response")] List<FixtureItem>? Response);

    private sealed record FixtureItem(
        [property: JsonPropertyName("fixture")] FixtureInfo Fixture,
        [property: JsonPropertyName("score")] ScoreInfo Score);

    private sealed record FixtureInfo(
        [property: JsonPropertyName("status")] StatusInfo Status);

    private sealed record StatusInfo(
        [property: JsonPropertyName("short")] string Short);

    private sealed record ScoreInfo(
        [property: JsonPropertyName("fulltime")] HalfTimeScore? FullTime);

    private sealed record HalfTimeScore(
        [property: JsonPropertyName("home")] int? Home,
        [property: JsonPropertyName("away")] int? Away);

    private sealed record DailyFixturesResponse(
        [property: JsonPropertyName("response")] List<DailyFixtureItem>? Response);

    private sealed record DailyFixtureItem(
        [property: JsonPropertyName("fixture")] DailyFixtureInfo Fixture,
        [property: JsonPropertyName("league")] DailyFixtureLeague League,
        [property: JsonPropertyName("teams")] DailyFixtureTeams Teams);

    private sealed record DailyFixtureInfo(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("date")] string Date);

    private sealed record DailyFixtureLeague(
        [property: JsonPropertyName("id")] int Id);

    private sealed record DailyFixtureTeams(
        [property: JsonPropertyName("home")] DailyFixtureTeam Home,
        [property: JsonPropertyName("away")] DailyFixtureTeam Away);

    private sealed record DailyFixtureTeam(
        [property: JsonPropertyName("name")] string Name);

    // ---------- Odds API wire types -------------------------------------------

    private sealed record OddsApiResponse(
        [property: JsonPropertyName("response")] List<OddsFixtureItem>? Response);

    private sealed record OddsFixtureItem(
        [property: JsonPropertyName("bookmakers")] List<BookmakerItem>? Bookmakers);

    private sealed class StringOrNumberConverter : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.Number)
                return reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            return reader.GetString() ?? string.Empty;
        }

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            string value,
            System.Text.Json.JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    private sealed record BookmakerItem(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("bets")] List<BetItem>? Bets);

    private sealed record BetItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("values")] List<BetValueItem>? Values);

    private sealed record BetValueItem(
        [property: JsonPropertyName("value"), JsonConverter(typeof(StringOrNumberConverter))] string Value,
        [property: JsonPropertyName("odd")] string Odd);
}
