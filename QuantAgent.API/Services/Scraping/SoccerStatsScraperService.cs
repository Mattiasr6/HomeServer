using System.Globalization;
using HtmlAgilityPack;

namespace QuantAgent.API.Services.Scraping;

/// <summary>
/// HTML scraper for SoccerStats.com. The site does not expose a JSON
/// API, so we fetch three tables per league and merge them by team name:
/// <list type="number">
///   <item><c>latest.asp?league=…</c> — league positions + W/D/L/GF/GA/points</item>
///   <item><c>table.asp?league=…&amp;tid=c</c> — goals tables (% Over 1.5/2.5/3.5, BTS)</item>
///   <item><c>table.asp?league=…&amp;tid=cr</c> — corners tables (avg corners scored/conceded, split by home/away)</item>
/// </list>
/// <para>
/// Realistic browser User-Agent and Accept headers are set in the
/// constructor; a 30-second per-request timeout is configured by the
/// host in <c>Program.cs</c>.
/// </para>
/// <para>
/// Registered as <c>Singleton</c> so the per-league cache lives for
/// the lifetime of the process. The cache uses case-insensitive
/// team-name keys because SoccerStats renders team names in
/// title-case and our DB may store them with different casing.
/// </para>
/// </summary>
internal sealed class SoccerStatsScraperService : ISoccerStatsScraperService
{
    private const string BaseUrl = "https://www.soccerstats.com/";

    private readonly HttpClient _http;
    private readonly ILogger<SoccerStatsScraperService> _logger;
    private readonly Dictionary<string, Dictionary<string, TeamStatsDto>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public SoccerStatsScraperService(HttpClient http, ILogger<SoccerStatsScraperService> logger)
    {
        _http = http;
        _logger = logger;

        // Realistic browser headers to avoid 403/Cloudflare blocks.
        // Set only if not already configured (idempotent for tests).
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9," +
                "image/avif,image/webp,*/*;q=0.8");
            _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        }
    }

    public async Task<TeamStatsDto?> GetTeamStatsAsync(
        string teamName,
        string league,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(league))
            return null;

        if (!_cache.TryGetValue(league, out var leagueData))
        {
            leagueData = await LoadLeagueAsync(league, cancellationToken);
            _cache[league] = leagueData;
            _logger.LogInformation(
                "SoccerStats cache miss for league '{League}': loaded {Count} teams",
                league, leagueData.Count);
        }

        if (leagueData.TryGetValue(teamName, out var stats))
            return stats;

        // Normalization fallback: alias map + fuzzy matching
        var normalized = TeamNameNormalizer.FindMatch(teamName, leagueData.Keys);
        if (normalized != null && leagueData.TryGetValue(normalized, out stats))
        {
            _logger.LogInformation(
                "SoccerStats name normalization: '{Raw}' → '{Normalized}'",
                teamName, normalized);
            return stats;
        }

        return null;
    }

    // ---------- league-level load (parallel 3-URL fan-out) ---------------------

    private async Task<Dictionary<string, TeamStatsDto>> LoadLeagueAsync(
        string league, CancellationToken ct)
    {
        var positionsTask = ScrapePositionsAsync(league, ct);
        var cornersTask = ScrapeCornersAsync(league, ct);
        var goalsTask = ScrapeGoalsAsync(league, ct);

        await Task.WhenAll(positionsTask, cornersTask, goalsTask);

        var positions = positionsTask.Result;
        var corners = cornersTask.Result;
        var goals = goalsTask.Result;

        var allTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in positions.Keys) allTeams.Add(k);
        foreach (var k in corners.Keys) allTeams.Add(k);
        foreach (var k in goals.Keys) allTeams.Add(k);

        var merged = new Dictionary<string, TeamStatsDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var team in allTeams)
        {
            positions.TryGetValue(team, out var pos);
            corners.TryGetValue(team, out var cor);
            goals.TryGetValue(team, out var over25);

            merged[team] = new TeamStatsDto(
                Posicion:        pos?.Posicion ?? 0,
                Puntos:          pos?.Puntos ?? 0,
                GolesFavor:      pos?.GolesFavor ?? 0,
                GolesContra:     pos?.GolesContra ?? 0,
                Over25:          over25 ?? "N/D",
                CornersLocal:    cor?.Local ?? 0.0,
                CornersVisitante: cor?.Visitante ?? 0.0);
        }

        return merged;
    }

    // ---------- positions (latest.asp) ------------------------------------------

    private async Task<Dictionary<string, (int Posicion, int Puntos, int GolesFavor, int GolesContra)?>>
        ScrapePositionsAsync(string league, CancellationToken ct)
    {
        var url = $"{BaseUrl}latest.asp?league={league}";
        var doc = await LoadDocAsync(url, ct);
        var result = new Dictionary<string, (int, int, int, int)?>(StringComparer.OrdinalIgnoreCase);
        if (doc == null) return result;

        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return result;

        var bannedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "LEAGUES", "MATCHES", "STATS", "HOME", "AWAY" };

        foreach (var table in tables)
        {
            var rows = table.SelectNodes(".//tr");
            if (rows == null || rows.Count < 15) continue;

            int position = 1;
            int teamsFound = 0;

            foreach (var row in rows.Skip(1))
            {
                var cells = row.SelectNodes(".//td|.//th");
                if (cells == null || cells.Count < 8) continue;

                var texts = cells.Select(c => HtmlEntity.DeEntitize(c.InnerText).Trim()).ToList();

                for (int i = 0; i < texts.Count; i++)
                {
                    var text = texts[i];
                    if (string.IsNullOrEmpty(text) || text.Length <= 2) continue;
                    if (text.All(char.IsDigit)) continue;
                    if (!text.Any(char.IsLetter)) continue;
                    if (bannedHeaders.Contains(text)) continue;

                    // Look for 6+ integers after the team name (MP, W, D, L, GF, GA).
                    var numbers = new List<int>();
                    for (int j = i + 1; j < texts.Count && numbers.Count < 6; j++)
                    {
                        if (int.TryParse(texts[j], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var n))
                            numbers.Add(n);
                    }

                    if (numbers.Count >= 6)
                    {
                        int mp = numbers[0], w = numbers[1], d = numbers[2],
                             l = numbers[3], gf = numbers[4], ga = numbers[5];
                        if (mp > 0 && (w + d + l) == mp && gf >= 0 && ga >= 0)
                        {
                            result[text] = (position, w * 3 + d, gf, ga);
                            position++;
                            teamsFound++;
                            break;
                        }
                    }
                }

                if (teamsFound >= 20) break;
            }

            if (teamsFound >= 15)
            {
                _logger.LogInformation(
                    "SoccerStats positions: extracted {Count} teams from {Url}", teamsFound, url);
                return result;
            }

            // Not enough rows in this table — try the next one.
            result.Clear();
        }

        _logger.LogWarning("SoccerStats positions: no valid league table found at {Url}", url);
        return result;
    }

    // ---------- corners (table.asp?tid=cr) --------------------------------------

    private async Task<Dictionary<string, (double Local, double Visitante)?>>
        ScrapeCornersAsync(string league, CancellationToken ct)
    {
        var url = $"{BaseUrl}table.asp?league={league}&tid=cr";
        var doc = await LoadDocAsync(url, ct);
        var result = new Dictionary<string, (double, double)?>(StringComparer.OrdinalIgnoreCase);
        if (doc == null) return result;

        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return result;

        foreach (var table in tables)
        {
            string? currentTipo = null;
            var ths = table.SelectNodes(".//th");
            if (ths != null)
            {
                foreach (var th in ths)
                {
                    var t = th.InnerText.ToLowerInvariant();
                    if (t.Contains("home") || t.Contains("hogar"))
                    {
                        currentTipo = "local";
                        break;
                    }
                    if (t.Contains("away") || t.Contains("lejos"))
                    {
                        currentTipo = "visitante";
                        break;
                    }
                }
            }
            if (currentTipo == null) continue;

            var rows = table.SelectNodes(".//tr");
            if (rows == null || rows.Count < 3) continue;

            foreach (var row in rows.Skip(2))
            {
                var columns = row.SelectNodes(".//td");
                if (columns == null || columns.Count < 7) continue;

                var equipo = HtmlEntity.DeEntitize(columns[0].InnerText).Trim();
                if (string.IsNullOrEmpty(equipo) ||
                    equipo.ToLowerInvariant().Contains("average"))
                    continue;

                if (!double.TryParse(
                        columns[2].InnerText.Trim().Replace(',', '.'),
                        NumberStyles.Any, CultureInfo.InvariantCulture,
                        out double cornersFavor))
                    continue;

                if (!result.TryGetValue(equipo, out var current))
                    current = (0.0, 0.0);

                if (currentTipo == "local")
                    result[equipo] = (cornersFavor, current!.Value.Item2);
                else
                    result[equipo] = (current!.Value.Item1, cornersFavor);
            }
        }

        _logger.LogInformation(
            "SoccerStats corners: extracted {Count} teams from {Url}", result.Count, url);
        return result;
    }

    // ---------- goals (table.asp?tid=c) -----------------------------------------

    private async Task<Dictionary<string, string>> ScrapeGoalsAsync(
        string league, CancellationToken ct)
    {
        var url = $"{BaseUrl}table.asp?league={league}&tid=c";
        var doc = await LoadDocAsync(url, ct);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (doc == null) return result;

        var table = doc.DocumentNode.SelectSingleNode("//table[@id='btable']");
        if (table == null)
        {
            _logger.LogWarning(
                "SoccerStats goals: table#btable not found at {Url}", url);
            return result;
        }

        var rows = table.SelectNodes(".//tr");
        if (rows == null) return result;

        foreach (var row in rows.Skip(1))
        {
            var columns = row.SelectNodes(".//td");
            if (columns == null || columns.Count < 10) continue;

            var team = HtmlEntity.DeEntitize(columns[0].InnerText).Trim();
            if (string.IsNullOrEmpty(team)) continue;

            // Column index 5 = "over_2_5" per the Python source.
            result[team] = HtmlEntity.DeEntitize(columns[5].InnerText).Trim();
        }

        _logger.LogInformation(
            "SoccerStats goals (Over 2.5): extracted {Count} teams from {Url}",
            result.Count, url);
        return result;
    }

    // ---------- HTTP helper ------------------------------------------------------

    private async Task<HtmlDocument?> LoadDocAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoccerStats: failed to fetch {Url}", url);
            return null;
        }
    }
}
