namespace QuantAgent.API.Services;

/// <summary>
/// Pure computation service for the Fractional Kelly Criterion.
/// Determines the optimal stake size given a bankroll, the model's
/// confidence, and the available decimal odds.
/// </summary>
public interface IBankrollManagementService
{
    /// <summary>
    /// Calculates the recommended stake using f/4 fractional Kelly.
    /// Returns 0 if the formula produces a negative value (no edge)
    /// or if the stake is less than 1 % of the bankroll.
    /// </summary>
    /// <param name="bankroll">Current available capital in your chosen unit.</param>
    /// <param name="confianza">Model confidence 0-100.</param>
    /// <param name="cuota">Decimal odds (e.g. 2.10).</param>
    /// <returns>Stake amount rounded to 2 decimals, or 0 if no bet recommended.</returns>
    decimal CalculateStake(decimal bankroll, int confianza, decimal cuota);
}
