namespace QuantAgent.API.Services;

/// <summary>
/// Compares odds from the primary (API-Football / Bet365) and secondary
/// (TheOddsAPI) sources. When the same market differs by more than 3 %
/// between sources OR across bookmakers, it raises an arbitrage or
/// data-inconsistency signal.
/// <para>
/// The 3 % threshold is applied as absolute difference in implied
/// probability (1/odds). This means a shift from 2.00 → 2.10
/// (50% → 47.6 %) triggers the flag, while 10.00 → 10.30
/// (10% → 9.7%) does not.
/// </para>
/// </summary>
public class OddsComparatorService
{
    private readonly ILogger<OddsComparatorService> _logger;

    /// <summary>
    /// Threshold for flagging a discrepancy. Expressed as an absolute
    /// difference in implied probability. 0.03 = 3 percentage points.
    /// </summary>
    private const double Threshold = 0.03;

    public OddsComparatorService(ILogger<OddsComparatorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compares Bet365 odds (the primary source used by FootballApiService)
    /// against all bookmaker odds from TheOddsAPI.
    /// Returns signals when a >3% implied-probability gap is detected.
    /// </summary>
    public OddsComparisonReport Compare(
        string homeTeam,
        string awayTeam,
        decimal bet365Local,
        decimal bet365Draw,
        decimal bet365Away,
        List<BookmakerOddsDto> alternativeOdds)
    {
        var signals = new List<ArbitrageSignal>();

        // Compare Bet365 vs each alternative bookmaker
        foreach (var alt in alternativeOdds)
        {
            var localDiff = ImpliedDiff(bet365Local, alt.CuotaLocal);
            var drawDiff  = ImpliedDiff(bet365Draw, alt.CuotaEmpate);
            var awayDiff  = ImpliedDiff(bet365Away, alt.CuotaVisita);

            if (localDiff > Threshold)
            {
                signals.Add(new ArbitrageSignal(
                    homeTeam, awayTeam, "Local",
                    bet365Local, alt.CuotaLocal, localDiff, alt.Bookmaker));
            }
            if (drawDiff > Threshold)
            {
                signals.Add(new ArbitrageSignal(
                    homeTeam, awayTeam, "Empate",
                    bet365Draw, alt.CuotaEmpate, drawDiff, alt.Bookmaker));
            }
            if (awayDiff > Threshold)
            {
                signals.Add(new ArbitrageSignal(
                    homeTeam, awayTeam, "Visitante",
                    bet365Away, alt.CuotaVisita, awayDiff, alt.Bookmaker));
            }
        }

        // Also detect intra-source arbitrage: within the alternative set,
        // check if any two bookmakers differ by >3% on the same outcome.
        var intraSignals = DetectIntraSourceArbitrage(
            homeTeam, awayTeam, alternativeOdds);
        signals.AddRange(intraSignals);

        if (signals.Count > 0)
        {
            _logger.LogWarning(
                "OddsComparator: {Count} arbitrage/data signals for {Home} vs {Away}",
                signals.Count, homeTeam, awayTeam);
        }

        return new OddsComparisonReport(
            homeTeam, awayTeam, signals.AsReadOnly());
    }

    /// <summary>
    /// Checks for discrepancies between bookmakers within the
    /// alternative-source dataset itself.
    /// </summary>
    private static List<ArbitrageSignal> DetectIntraSourceArbitrage(
        string homeTeam, string awayTeam, List<BookmakerOddsDto> odds)
    {
        var signals = new List<ArbitrageSignal>();
        if (odds.Count < 2) return signals;

        for (int i = 0; i < odds.Count; i++)
        {
            for (int j = i + 1; j < odds.Count; j++)
            {
                var a = odds[i];
                var b = odds[j];

                CheckOutcome(homeTeam, awayTeam, "Local",
                    a.CuotaLocal, b.CuotaLocal, a.Bookmaker, b.Bookmaker, signals);
                CheckOutcome(homeTeam, awayTeam, "Empate",
                    a.CuotaEmpate, b.CuotaEmpate, a.Bookmaker, b.Bookmaker, signals);
                CheckOutcome(homeTeam, awayTeam, "Visitante",
                    a.CuotaVisita, b.CuotaVisita, a.Bookmaker, b.Bookmaker, signals);
            }
        }

        return signals;
    }

    private static void CheckOutcome(
        string homeTeam, string awayTeam, string outcome,
        decimal priceA, decimal priceB,
        string bookmakerA, string bookmakerB,
        List<ArbitrageSignal> signals)
    {
        if (priceA <= 0 || priceB <= 0) return;
        var diff = ImpliedDiff(priceA, priceB);
        if (diff > Threshold)
        {
            signals.Add(new ArbitrageSignal(
                homeTeam, awayTeam, outcome,
                priceA, priceB, diff, $"{bookmakerA} vs {bookmakerB}"));
        }
    }

    /// <summary>
    /// Absolute difference in implied probability between two decimal odds.
    /// </summary>
    private static double ImpliedDiff(decimal oddsA, decimal oddsB)
    {
        if (oddsA <= 0 || oddsB <= 0) return 0;
        var probA = 1.0 / (double)oddsA;
        var probB = 1.0 / (double)oddsB;
        return Math.Abs(probA - probB);
    }
}

/// <summary>
/// Describes a single detected discrepancy between two odds sources
/// for the same match outcome.
/// </summary>
public record ArbitrageSignal(
    string HomeTeam,
    string AwayTeam,
    string Outcome,
    decimal OddsSourceA,
    decimal OddsSourceB,
    double ImpliedProbDiff,
    string SourceLabel);

/// <summary>
/// Aggregated report for one match comparison.
/// </summary>
public record OddsComparisonReport(
    string HomeTeam,
    string AwayTeam,
    IReadOnlyCollection<ArbitrageSignal> Signals);
