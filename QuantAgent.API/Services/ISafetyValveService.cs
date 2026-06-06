using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Services;

/// <summary>
/// Monitors daily losses and acts as a circuit breaker.
/// When the cumulative lost stake exceeds 5% of bankroll, the
/// system enters EMERGENCY_HALT — all new predictions and
/// Telegram notifications are suppressed.
/// <para>
/// The halt is evaluated on every check and auto-releases when
/// the daily loss falls back under the threshold (e.g. after
/// the next daily roll-over).
/// </para>
/// </summary>
public interface ISafetyValveService
{
    /// <summary>
    /// Sum of <c>StakeSugerido</c> for all lost predictions today.
    /// </summary>
    Task<decimal> GetDailyLossAsync(CancellationToken ct = default);

    /// <summary>
    /// Current system status based on daily loss vs bankroll * 5%.
    /// </summary>
    Task<SystemStatus> GetSystemStatusAsync(CancellationToken ct = default);
}
