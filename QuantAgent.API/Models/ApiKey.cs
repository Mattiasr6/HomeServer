using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Models;

/// <summary>
/// An API key for an external sports data provider.
/// The key rotation system tracks usage metrics and automatically
/// marks keys as <see cref="EstadoApiKey.Limitada"/> when the
/// provider returns 429 (Rate Limit Exceeded).
/// </summary>
public class ApiKey : BaseEntity
{
    /// <summary>
    /// The raw credential (e.g. "abc123def456...").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The sport or domain this key is scoped to
    /// (e.g. "football", "basketball").
    /// </summary>
    public string Deporte { get; set; } = string.Empty;

    /// <summary>
    /// Current lifecycle state.
    /// </summary>
    public EstadoApiKey Estado { get; set; } = EstadoApiKey.Activa;

    /// <summary>
    /// Timestamp of the most recent API call using this key.
    /// </summary>
    public DateTime UltimoUso { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Running count of successful API calls.
    /// </summary>
    public int Exitos { get; set; }

    /// <summary>
    /// Running count of failed API calls.
    /// </summary>
    public int Fallos { get; set; }
}
