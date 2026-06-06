namespace QuantAgent.API.Services.Scraping;

/// <summary>
/// Consolidated per-team statistics scraped from SoccerStats.com.
/// All values are season-to-date aggregates; semantics are documented
/// per-property because SoccerStats does not expose machine-readable
/// column metadata.
/// </summary>
/// <param name="Posicion">League position (1 = leader).</param>
/// <param name="Puntos">Total points (W*3 + D).</param>
/// <param name="GolesFavor">Goals scored.</param>
/// <param name="GolesContra">Goals conceded.</param>
/// <param name="Over25">Raw text of the "% matches Over 2.5 goals" cell, e.g. "65%".</param>
/// <param name="CornersLocal">Average corners scored by this team when playing at home.</param>
/// <param name="CornersVisitante">Average corners scored by this team when playing away.</param>
public record TeamStatsDto(
    int Posicion,
    int Puntos,
    int GolesFavor,
    int GolesContra,
    string Over25,
    double CornersLocal,
    double CornersVisitante);
