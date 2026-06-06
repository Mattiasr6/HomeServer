using QuantAgent.API.Data;

namespace QuantAgent.API.Models;

/// <summary>
/// A learned heuristic the agent accumulates over time.
/// <see cref="Peso"/> grows when the rule produces correct
/// predictions and shrinks (or is pruned) otherwise.
/// </summary>
public class ReglaAprendida : BaseEntity
{

    public Guid? PrediccionId { get; set; }
    public Prediccion? Prediccion { get; set; }
    public string Equipo { get; set; } = string.Empty;

    public string Contexto { get; set; } = string.Empty;

    public string Regla { get; set; } = string.Empty;

    public int Peso { get; set; } = 1;
}
