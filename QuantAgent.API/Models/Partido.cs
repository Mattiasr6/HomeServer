using System;
using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Models;

/// <summary>
/// A sports match tracked by the quantitative agent.
/// Score fields are nullable because a match is created in
/// <see cref="EstadoPartido.Pendiente"/> and is filled in
/// once the match is settled.
/// </summary>
public class Partido : BaseEntity
{
    public string EquipoLocal { get; set; } = string.Empty;

    public string EquipoVisitante { get; set; } = string.Empty;

    public DateTime FechaInicio { get; set; }

    public EstadoPartido Estado { get; set; } = EstadoPartido.Pendiente;

    public int? GolesLocal { get; set; }

    public int? GolesVisitante { get; set; }

    /// <summary>
    /// API-Football fixture ID used to fetch real results in Phase B.
    /// Null for simulated/legacy matches.
    /// </summary>
    public int? FixtureId { get; set; }
}
