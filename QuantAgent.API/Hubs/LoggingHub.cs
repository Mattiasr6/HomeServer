using Microsoft.AspNetCore.SignalR;

namespace QuantAgent.API.Hubs;

/// <summary>
/// SignalR Hub used to stream real-time pipeline logs to the
/// Quant Command Center frontend dashboard.
///
/// Connected clients receive:
///   <c>OnLog</c> — a log entry with timestamp, level and message.
/// </summary>
public class LoggingHub : Hub
{
    public const string Route = "/hubs/logging";

    /// <summary>Received by clients as "OnLog".</summary>
    public const string LogEvent = "OnLog";
}
