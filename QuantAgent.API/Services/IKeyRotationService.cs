using QuantAgent.API.Models;

namespace QuantAgent.API.Services;

/// <summary>
/// Manages API key lifecycle: provides the best available key
/// for a given sport, records usage outcomes, and handles
/// automatic rotation when a key is rate-limited.
/// </summary>
public interface IKeyRotationService
{
    /// <summary>
    /// Returns the most suitable active key for the given sport.
    /// Selection strategy: lowest failure count among non-Limitada keys.
    /// Throws <see cref="InvalidOperationException"/> if no valid key exists.
    /// </summary>
    Task<ApiKey> GetValidKeyAsync(string deporte);

    /// <summary>
    /// Marks a key as rate-limited (429 response from provider).
    /// The key reactor (KeyHealthCheckJob) will periodically test
    /// limited keys and reactivate them when the rate window resets.
    /// </summary>
    Task MarkAsLimitedAsync(Guid keyId);

    /// <summary>
    /// Records a successful API usage (increments Exitos, updates UltimoUso).
    /// </summary>
    Task RecordSuccessAsync(Guid keyId);

    /// <summary>
    /// Records a failed API usage (increments Fallos, updates UltimoUso).
    /// </summary>
    Task RecordFailureAsync(Guid keyId);
}
