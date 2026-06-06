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
}
