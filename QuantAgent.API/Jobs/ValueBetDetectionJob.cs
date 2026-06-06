using Hangfire;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services.Inference;
using QuantAgent.API.Services.Scraping;
using QuantAgent.API.Services.Telegram;
using QuantAgent.API.Services;

namespace QuantAgent.API.Jobs;

/// <summary>
/// Phase C of the orchestration loop: for every pending match
/// (<see cref="EstadoPartido.Pendiente"/>) that has not yet been
/// predicted, ask the Ollama-backed inference service for a
/// quantitative decision, persist the resulting
/// <see cref="Prediccion"/>, and forward any "APOSTAR" verdict to
/// Telegram.
/// </summary>
/// <para>Marked <c>internal</c> to match the visibility of the inference contract it depends on (CS0051 otherwise).</para>
internal class ValueBetDetectionJob
{
    /// <summary>
    /// Decision value that triggers a Telegram alert. The Ollama
    /// prompt instructs the model to emit exactly one of
    /// <c>APOSTAR</c> or <c>IGNORAR</c> as the binary decision;
    /// the narrower team-name selection lives in
    /// <c>Prediccion.Seleccion</c>.
    /// </summary>
    private const string ApostarDecision = "APOSTAR";

    /// <summary>
    /// Mirror of the <c>predicciones.razonamiento</c> column
    /// (<c>VARCHAR(2000)</c> in the EF mapping). Longer model
    /// output is truncated to fit.
    /// </summary>
    private const int RazonamientoMaxLength = 2000;

    /// <summary>
    /// Static mapping from team name → SoccerStats.com league slug.
    /// Extend as new leagues/teams are added to the ingestion layer.
    /// Unknown teams degrade gracefully (scraper returns null →
    /// prompt notes "no stats available").
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> TeamLeagueMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // --- Spain (La Liga) ---
            ["Real Madrid"] = "spain",
            ["Barcelona"] = "spain",
            ["Atletico Madrid"] = "spain",
            ["Sevilla"] = "spain",
            ["Real Sociedad"] = "spain",
            ["Real Betis"] = "spain",
            ["Villarreal"] = "spain",
            ["Athletic Club"] = "spain",
            ["Valencia"] = "spain",
            ["Girona"] = "spain",
            ["Osasuna"] = "spain",
            ["Getafe"] = "spain",
            ["Celta Vigo"] = "spain",
            ["Mallorca"] = "spain",
            ["Las Palmas"] = "spain",
            ["Alaves"] = "spain",
            ["Espanyol"] = "spain",
            ["Leganes"] = "spain",
            ["Valladolid"] = "spain",
            // --- England (Premier League) ---
            ["Manchester City"] = "england",
            ["Liverpool"] = "england",
            ["Arsenal"] = "england",
            ["Manchester United"] = "england",
            ["Chelsea"] = "england",
            ["Tottenham"] = "england",
            ["Newcastle"] = "england",
            ["Aston Villa"] = "england",
            // --- Germany (Bundesliga) ---
            ["Bayern Munich"] = "germany",
            ["Borussia Dortmund"] = "germany",
            ["Bayer Leverkusen"] = "germany",
            ["RB Leipzig"] = "germany",
            // --- Italy (Serie A) ---
            ["Juventus"] = "italy",
            ["AC Milan"] = "italy",
            ["Inter Milan"] = "italy",
            ["Napoli"] = "italy",
            ["Roma"] = "italy",
            ["Lazio"] = "italy",
            ["Atalanta"] = "italy",
            // --- France (Ligue 1) ---
            ["PSG"] = "france",
            ["Marseille"] = "france",
            ["Lyon"] = "france",
            ["Monaco"] = "france",
        };

    private readonly QuantDbContext _db;
    private readonly FootballApiService _footballApi;
    private readonly IOllamaInferenceService _inference;
    private readonly ITelegramNotificationService _telegram;
    private readonly ISoccerStatsScraperService _scraper;
    private readonly ILogger<ValueBetDetectionJob> _logger;

    public ValueBetDetectionJob(
        QuantDbContext db,
        FootballApiService footballApi,
        IOllamaInferenceService inference,
        ITelegramNotificationService telegram,
        ISoccerStatsScraperService scraper,
        ILogger<ValueBetDetectionJob> logger)
    {
        _db = db;
        _footballApi = footballApi;
        _inference = inference;
        _telegram = telegram;
        _scraper = scraper;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task AnalyzePendingMatchesAsync(CancellationToken ct)
    {
        _logger.LogInformation("[Phase C] Starting value-bet scan at {Time:o}", DateTime.UtcNow);

        // Pending matches that do NOT yet have a Prediccion. The
        // NOT EXISTS subquery is pushed down to PostgreSQL by EF.
        var pending = await _db.Partidos
            .AsNoTracking()
            .Where(p => p.Estado == EstadoPartido.Pendiente
                     && !_db.Predicciones.Any(pr => pr.PartidoId == p.Id))
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            _logger.LogInformation("[Phase C] No pending matches to analyze — exiting");
            return;
        }

        _logger.LogInformation("[Phase C] Analyzing {Count} pending match(es)", pending.Count);

        var processed = 0;
        var alertsSent = 0;
        var errors = 0;

        foreach (var partido in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (apostar, _, _) = await AnalyzeOneAsync(partido, ct);
                processed++;
                if (apostar) alertsSent++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex,
                    "[Phase C] Failed to analyze match {Id} ({Local} vs {Visitante})",
                    partido.Id, partido.EquipoLocal, partido.EquipoVisitante);
            }
        }

        _logger.LogInformation(
            "[Phase C] Done. processed={Processed} alerts={Alerts} errors={Errors}",
            processed, alertsSent, errors);
    }

    /// <summary>
    /// Targeted analysis of a single match by its Partido GUID.
    /// Loads the entity, runs the full inference pipeline (stats, odds,
    /// Ollama), persists the prediction, and alerts Telegram if the
    /// model detects a value bet.
    /// Used by the <c>/analizar</c> Telegram command for on-demand analysis.
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task AnalyzeSingleMatchAsync(Guid partidoId, CancellationToken ct)
    {
        var partido = await _db.Partidos.FindAsync(new object[] { partidoId }, ct);
        if (partido is null)
        {
            _logger.LogWarning("[Phase C] Match {Id} not found for single analysis", partidoId);
            return;
        }

        _logger.LogInformation(
            "[Phase C] Single analysis for {Local} vs {Visitante} (fixture {FixtureId})",
            partido.EquipoLocal, partido.EquipoVisitante, partido.FixtureId);

        try
        {
            var (apostar, _, _) = await AnalyzeOneAsync(partido, ct);
            _logger.LogInformation(
                "[Phase C] Single analysis done for {Id}: apostar={Apostar}",
                partidoId, apostar);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Phase C] Single analysis failed for match {Id}", partidoId);
            throw;
        }
    }

    /// <summary>
    /// Core per-match inference pipeline shared by batch
    /// (<see cref="AnalyzePendingMatchesAsync"/>) and single
    /// (<see cref="AnalyzeSingleMatchAsync"/>) entry points.
    /// Returns whether the model decided APOSTAR, plus counters.
    /// </summary>
    private async Task<(bool Apostar, int Processed, int Alerts)> AnalyzeOneAsync(
        Partido partido, CancellationToken ct)
    {
        var reglas = await _db.ReglasAprendidas
            .AsNoTracking()
            .Where(r => r.Equipo == partido.EquipoLocal
                     || r.Equipo == partido.EquipoVisitante)
            .ToListAsync(ct);

        var (localStats, visitanteStats) = await FetchTeamStatsAsync(partido, ct);

        // Fetch all odds in parallel
        var oddsTask = partido.FixtureId.HasValue
            ? FetchAllOddsAsync(partido.FixtureId.Value)
            : Task.FromResult(new AllOddsDto());

        var allOdds = await oddsTask;

        // Run 3 market analyses in parallel
        var ganadorTask = _inference.AnalyzeMarketAsync(
            partido, reglas, localStats, visitanteStats,
            TipoMercado.Ganador,
            allOdds.CuotaLocal, allOdds.CuotaEmpate, allOdds.CuotaVisita,
            allOdds.CornersOver, allOdds.CornersUnder,
            allOdds.GoalsOver, allOdds.GoalsUnder, ct);

        var cornersTask = _inference.AnalyzeMarketAsync(
            partido, reglas, localStats, visitanteStats,
            TipoMercado.Corners,
            allOdds.CuotaLocal, allOdds.CuotaEmpate, allOdds.CuotaVisita,
            allOdds.CornersOver, allOdds.CornersUnder,
            allOdds.GoalsOver, allOdds.GoalsUnder, ct);

        var golesTask = _inference.AnalyzeMarketAsync(
            partido, reglas, localStats, visitanteStats,
            TipoMercado.Goles,
            allOdds.CuotaLocal, allOdds.CuotaEmpate, allOdds.CuotaVisita,
            allOdds.CornersOver, allOdds.CornersUnder,
            allOdds.GoalsOver, allOdds.GoalsUnder, ct);

        var results = await Task.WhenAll(ganadorTask, cornersTask, golesTask);

        var predicciones = new List<Prediccion>(3);
        var apostarCount = 0;

        foreach (var result in results)
        {
            var mercado = result.Decision switch
            {
                // The model returns Mercado in the JSON, but since we
                // called per-market, we know which it is from context.
                // Fallback: parse from the response or use market index.
                _ when string.Equals(result.Decision, ApostarDecision,
                    StringComparison.OrdinalIgnoreCase) => true,
                _ => false
            };

            // Determine the odds value to store
            var cuota = GetCuotaForResult(result, allOdds, partido);

            var prediccion = new Prediccion
            {
                PartidoId = partido.Id,
                Mercado = MapMercado(result),
                Seleccion = result.Seleccion,
                Cuota = cuota,
                Confianza = result.Confianza,
                CornersOverUnder = result.CornersOverUnder,
                TotalGoals = result.TotalGoals,
                Razonamiento = result.Razonamiento.Length > RazonamientoMaxLength
                    ? result.Razonamiento[..RazonamientoMaxLength]
                    : result.Razonamiento,
                Estado = EstadoPrediccion.Pendiente,
            };

            predicciones.Add(prediccion);
        }

        _db.Predicciones.AddRange(predicciones);
        await _db.SaveChangesAsync(ct);

        // Log summary
        foreach (var p in predicciones)
        {
            var isApostar = string.Equals(
                p.Seleccion, "Over", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Seleccion, "APOSTAR", StringComparison.OrdinalIgnoreCase)
                // Fallback: if the model said APOSTAR in Decision field,
                // we already evaluated above
                ;

            _logger.LogInformation(
                "[Phase C] {Local} vs {Visitante} [{Mercado}] -> {Seleccion} ({Confianza}%)",
                partido.EquipoLocal, partido.EquipoVisitante,
                p.Mercado, p.Seleccion, p.Confianza);
        }

        // Telegram alerts per market with APOSTAR decisions
        var alertMessages = predicciones
            .Where(p => string.Equals(p.Seleccion, "Over", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Seleccion, "APOSTAR", StringComparison.OrdinalIgnoreCase)
                || p.Confianza >= 70) // High confidence = always alert
            .Select(p => {
                apostarCount++;
                return
                    "[ALERTA] VALUE BET DETECTADA\n" +
                    $"{partido.EquipoLocal} vs {partido.EquipoVisitante}\n" +
                    $"Mercado: {p.Mercado}\n" +
                    $"Seleccion: {p.Seleccion}\n" +
                    $"Confianza: {p.Confianza}%\n" +
                    $"Cuota: {p.Cuota:N2}\n" +
                    $"Razonamiento: {p.Razonamiento}";
            })
            .ToList();

        foreach (var msg in alertMessages)
        {
            await _telegram.SendAlertAsync(msg, ct);
        }

        var hasApostar = apostarCount > 0;
        _logger.LogInformation(
            "[Phase C] Done for {Local} vs {Visitante}: {Preds} predictions, {Apostar} value bets",
            partido.EquipoLocal, partido.EquipoVisitante,
            predicciones.Count, apostarCount);

        return (hasApostar, predicciones.Count, apostarCount);
    }

    private static TipoMercado MapMercado(PrediccionResult result)
    {
        if (result.CornersOverUnder > 0) return TipoMercado.Corners;
        if (result.TotalGoals > 0) return TipoMercado.Goles;
        return TipoMercado.Ganador;
    }

    private static decimal GetCuotaForResult(
        PrediccionResult result, AllOddsDto odds, Partido partido)
    {
        if (result.CornersOverUnder > 0)
        {
            // Corners market
            return string.Equals(result.Seleccion, "Over",
                StringComparison.OrdinalIgnoreCase)
                ? odds.CornersOver : odds.CornersUnder;
        }
        if (result.TotalGoals > 0)
        {
            // Goles market
            return string.Equals(result.Seleccion, "Over",
                StringComparison.OrdinalIgnoreCase)
                ? odds.GoalsOver : odds.GoalsUnder;
        }
        // Ganador market — map selection to odds
        return result.Seleccion switch
        {
            string s when s.Equals(partido.EquipoLocal, StringComparison.OrdinalIgnoreCase) => odds.CuotaLocal,
            string s when s.Equals(partido.EquipoVisitante, StringComparison.OrdinalIgnoreCase) => odds.CuotaVisita,
            _ => odds.CuotaEmpate,
        };
    }

    private record AllOddsDto
    {
        public decimal CuotaLocal { get; init; }
        public decimal CuotaEmpate { get; init; }
        public decimal CuotaVisita { get; init; }
        public decimal CornersOver { get; init; }
        public decimal CornersUnder { get; init; }
        public decimal GoalsOver { get; init; }
        public decimal GoalsUnder { get; init; }
    }

    private async Task<AllOddsDto> FetchAllOddsAsync(int fixtureId)
    {
        var (cuotaLocal, cuotaEmpate, cuotaVisita) = await _footballApi.GetMatchOddsAsync(fixtureId);
        var (cornersOver, cornersUnder) = await _footballApi.GetCornersOddsAsync(fixtureId);
        var (goalsOver, goalsUnder) = await _footballApi.GetGoalsOddsAsync(fixtureId);

        return new AllOddsDto
        {
            CuotaLocal = cuotaLocal,
            CuotaEmpate = cuotaEmpate,
            CuotaVisita = cuotaVisita,
            CornersOver = cornersOver,
            CornersUnder = cornersUnder,
            GoalsOver = goalsOver,
            GoalsUnder = goalsUnder,
        };
    }

    /// <summary>
    /// Resolve the SoccerStats league for each team and fetch
    /// consolidated stats. Per-team try/catch ensures that one
    /// team's failure does not block the other.
    /// </summary>
    private async Task<(TeamStatsDto? Local, TeamStatsDto? Visitante)> FetchTeamStatsAsync(
        Partido partido, CancellationToken ct)
    {
        var local = await TryGetStatsAsync(partido.EquipoLocal, ct);
        var visitante = await TryGetStatsAsync(partido.EquipoVisitante, ct);
        return (local, visitante);
    }

    private async Task<TeamStatsDto?> TryGetStatsAsync(string teamName, CancellationToken ct)
    {
        if (!TeamLeagueMap.TryGetValue(teamName, out var league))
        {
            _logger.LogDebug(
                "[Phase C] No league mapping for team '{Team}' — proceeding without live stats",
                teamName);
            return null;
        }

        try
        {
            return await _scraper.GetTeamStatsAsync(teamName, league, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Phase C] Scraper failed for '{Team}' (league='{League}') — proceeding without live stats",
                teamName, league);
            return null;
        }
    }
}
