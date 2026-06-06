namespace QuantAgent.API.Models.Enums;

/// <summary>
/// Operational status of the quantitative engine.
/// When EMERGENCY_HALT is active, all new bets and Telegram
/// notifications are suppressed until the next daily reset.
/// </summary>
public enum SystemStatus
{
    /// <summary>Normal operation — bets and alerts proceed.</summary>
    NORMAL = 0,

    /// <summary>Daily loss exceeds 5% of bankroll — all betting halted.</summary>
    EMERGENCY_HALT = 1
}
