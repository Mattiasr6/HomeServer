namespace QuantAgent.API.Services;

/// <summary>
/// Secondary odds feed (TheOddsAPI) that provides multi-bookmaker
/// odds for cross-validation and arbitrage detection against the
/// primary API-Football feed.
/// </summary>
public interface IAlternativeOddsService
{
    /// <summary>
    /// Returns decimal odds for the Match Winner (1x2) market across
    /// ALL bookmakers that TheOddsAPI returns for the given fixture.
    /// Returns an empty list when the fixture is not found or odds
    /// are not yet published.
    /// </summary>
    Task<List<BookmakerOddsDto>> GetMatchWinnerOddsAsync(
        string homeTeam, string awayTeam, int sportId, CancellationToken ct = default);
}

/// <summary>
/// Normalised odds from a single bookmaker, regardless of source.
/// </summary>
public record BookmakerOddsDto(
    string Bookmaker,
    decimal CuotaLocal,
    decimal CuotaEmpate,
    decimal CuotaVisita);
