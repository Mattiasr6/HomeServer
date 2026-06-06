using Microsoft.AspNetCore.SignalR;
using QuantAgent.API.Hubs;

namespace QuantAgent.API.Services;

/// <summary>
/// Background service that broadcasts a SignalR heartbeat event every
/// 60 seconds so the dashboard can detect connection loss.
/// <para>
/// Clients listen for the <c>OnHeartbeat</c> event. If no event is
/// received for >60 seconds, the dashboard shows a "Connection Lost"
/// alert in the UI.
/// </para>
/// </summary>
public class HeartbeatService : BackgroundService
{
    private readonly IHubContext<LoggingHub> _hub;
    private readonly ILogger<HeartbeatService> _logger;

    private const int IntervalSeconds = 60;

    public HeartbeatService(
        IHubContext<LoggingHub> hub,
        ILogger<HeartbeatService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat service started (every {S}s)", IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var payload = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    status = "alive",
                };

                await _hub.Clients.All.SendAsync("OnHeartbeat", payload, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Heartbeat broadcast failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
        }
    }
}
