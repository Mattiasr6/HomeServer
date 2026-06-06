namespace QuantAgent.API.Services;

/// <summary>
/// Real-time telemetry broadcaster. Injected into jobs and services
/// so every critical pipeline step can stream a log entry to all
/// connected SignalR dashboard clients.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Broadcasts a structured log entry to all connected dashboard clients.
    /// </summary>
    /// <param name="message">The log text.</param>
    /// <param name="level">Severity label: "INFO", "AI", "ERROR".</param>
    Task BroadcastLogAsync(string message, string level = "INFO");
}
