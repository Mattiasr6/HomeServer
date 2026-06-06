using QuantAgent.API.Models;

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
    /// <summary>
    /// Sends a structured value-bet alert to the configured administrator.
    /// Includes market-specific emoji, confidence %, odds, and inline feedback buttons.
    /// </summary>
    /// <param name="prediccion">The prediction that qualified as a value bet.</param>
    /// <param name="partido">The associated match for context (teams, link).</param>
    /// <param name="ct">Cancellation token to abort the send.</param>
    Task SendValueBetAlertAsync(Prediccion prediccion, Partido partido, CancellationToken ct = default);

    /// <summary>
    /// Sends a discrepancy alert when the odds comparator detects
    /// a >10% implied-probability gap between primary and secondary
    /// sources. The message includes the match, the affected outcome,
    /// the discrepancy percentage, and asks for confirmation.
    /// </summary>
    /// <param name="prediccion">The affected prediction.</param>
    /// <param name="partido">Match for context.</param>
    /// <param name="diffPct">The absolute implied-probability difference as a percentage (e.g. 12.5 for 12.5%).</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendDiscrepancyAlertAsync(Prediccion prediccion, Partido partido, double diffPct, CancellationToken ct = default);

}
