namespace QuantAgent.API.Services.Telegram;

/// <summary>
/// Outbound channel to Telegram. The implementation is responsible
/// for routing alerts to the configured administrator chat and
/// handling transient send failures (retry / log).
/// </summary>
public interface ITelegramNotificationService
{
    /// <summary>
    /// Sends a free-form text alert to the configured administrator.
    /// </summary>
    /// <param name="message">Plain text body of the alert.</param>
    /// <param name="ct">Cancellation token to abort the send.</param>
    Task SendAlertAsync(string message, CancellationToken ct = default);
}
