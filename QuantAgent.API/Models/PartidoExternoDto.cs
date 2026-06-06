namespace QuantAgent.API.Models;

/// <summary>
/// Lightweight DTO representing a fixture from the external API-Football feed.
/// Used to transfer raw API data into <see cref="Partido"/> entities.
/// </summary>
public record PartidoExternoDto(
    int FixtureId,
    string EquipoLocal,
    string EquipoVisitante,
    DateTime FechaInicio,
    int LeagueId);
