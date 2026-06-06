namespace QuantAgent.API.Services;

/// <summary>
/// Fractional Kelly Criterion (f/4) for stake sizing.
///
/// Formula:
///   f* = (b·p − q) / b    (pure Kelly)
///   f*/4                   (fractional — 1/4 Kelly)
///
/// Where:
///   b = cuota − 1
///   p = confianza / 100
///   q = 1 − p
///
/// Edge cases:
///   • Returns 0 if the formula produces a negative number (no edge).
///   • Returns 0 if the stake would be less than 1 % of the bankroll.
///   • Returns 0 if inputs are out of range (cuota ≤ 1, confianza ≤ 0, etc.).
/// </summary>
public class BankrollManagementService : IBankrollManagementService
{
    private const decimal FractionalFactor = 4m;         // f/4 Kelly
    private const decimal MinStakeFraction = 0.01m;      // 1 % of bankroll

    public decimal CalculateStake(decimal bankroll, int confianza, decimal cuota)
    {
        // Guard clauses — no edge possible
        if (bankroll <= 0) return 0m;
        if (cuota <= 1m)   return 0m;
        if (confianza is <= 0 or > 100) return 0m;

        // Kelly inputs
        var b = cuota - 1m;                // net odds
        var p = confianza / 100m;          // implied probability from model
        var q = 1m - p;                    // probability of loss

        // Pure Kelly fraction
        var kellyPure = (b * p - q) / b;

        // Negative Kelly = no edge
        if (kellyPure <= 0) return 0m;

        // Apply fractional factor to reduce variance
        var stakeFraction = kellyPure / FractionalFactor;
        var stake = bankroll * stakeFraction;

        // Minimum threshold — skip if the bet is too small to matter
        if (stake < bankroll * MinStakeFraction) return 0m;

        return Math.Round(stake, 2);
    }
}
