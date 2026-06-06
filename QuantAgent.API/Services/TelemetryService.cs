using Microsoft.AspNetCore.SignalR;
using QuantAgent.API.Hubs;

namespace QuantAgent.API.Services;

/// <summary>
/// Default <see cref="ITelemetryService"/> — uses <see cref="IHubContext{LoggingHub}"/>
/// to push log entries to every connected SignalR client in real time.
///
/// Registered as Singleton (IHubContext is thread-safe and resolved once).
/// </summary>
public class TelemetryService : ITelemetryService
{
    private readonly IHubContext<LoggingHub> _hub;

    public TelemetryService(IHubContext<LoggingHub> hub)
    {
        _hub = hub;
    }

    public async Task BroadcastLogAsync(string message, string level = "INFO")
    {
        var entry = new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            level,
            message,
        };

        await _hub.Clients.All.SendAsync(LoggingHub.LogEvent, entry);
    }
}
