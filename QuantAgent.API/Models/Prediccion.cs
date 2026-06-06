using System;
using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Models;

/// <summary>
/// A prediction emitted by the agent for a given <see cref="Partido"/>.
/// Stored independently from the match so we can version, audit and
/// reconcile outcomes after the match is finalized.
/// </summary>
public class Prediccion : BaseEntity
{
    public Guid PartidoId { get; set; }

    public Partido? Partido { get; set; }

    /// <summary>
    /// The market selection the agent is backing
    /// (e.g. "Local", "Visitante", "Empate", "Over 2.5").
    /// </summary>
    public string Seleccion { get; set; } = string.Empty;

    public decimal Cuota { get; set; }

    /// <summary>
    /// Agent's self-assessed confidence in the prediction, 0-100.
    /// </summary>
    public int Confianza { get; set; }

    public string Razonamiento { get; set; } = string.Empty;

    public EstadoPrediccion Estado { get; set; } = EstadoPrediccion.Pendiente;

    /// <summary>
    /// The market type this prediction targets (Ganador, Corners, Goles).
    /// </summary>
    public TipoMercado Mercado { get; set; } = TipoMercado.Ganador;

    /// <summary>
    /// For Corners market: the Over/Under threshold (e.g. 9.5 means "Over 9.5").
    /// Zero when not applicable.
    /// </summary>
    public decimal CornersOverUnder { get; set; }

    /// <summary>
    /// For Goles market: the Over/Under threshold (e.g. 2.5 means "Over 2.5").
    /// Zero when not applicable.
    /// </summary>
    public decimal TotalGoals { get; set; }

    /// <summary>
    /// Recommended stake amount using fractional Kelly Criterion.
    /// Zero means "no bet" — the edge is below the minimum threshold.
    /// </summary>
    public decimal StakeSugerido { get; set; }

    /// <summary>
    /// When true, the odds comparison detected a >10% implied-probability
    /// discrepancy between primary and secondary sources. The dashboard
    /// displays these predictions in yellow/red, and Telegram asks the
    /// administrator to confirm before proceeding.
    /// </summary>
    public bool DataAnomaly { get; set; }
}
