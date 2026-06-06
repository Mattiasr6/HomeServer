using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Services;

/// <summary>
/// Computes daily loss from the Predicciones table and compares it
/// against the configured bankroll to determine system status.
/// <para>
/// Registered as Singleton. DB access is scoped via IServiceScopeFactory
/// to avoid captive dependency in the singleton host.
/// </para>
/// </summary>
public class SafetyValveService : ISafetyValveService
{
    private const decimal HaltRatio = 0.05m;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SafetyValveService> _logger;

    public SafetyValveService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SafetyValveService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<decimal> GetDailyLossAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        var todayStart = DateTime.UtcNow.Date;
        var loss = await db.Predicciones
            .Where(p => p.Estado == EstadoPrediccion.Perdida
                     && p.UpdatedAt >= todayStart)
            .SumAsync(p => (decimal?)p.StakeSugerido, ct) ?? 0m;

        return loss;
    }

    public async Task<SystemStatus> GetSystemStatusAsync(CancellationToken ct = default)
    {
        var loss = await GetDailyLossAsync(ct);
        var bankroll = _configuration.GetValue<decimal>("Bankroll:Total", 1000m);
        var threshold = bankroll * HaltRatio;

        if (loss > threshold)
        {
            _logger.LogWarning(
                "SAFETY VALVE: Daily loss {Loss:N2} exceeds {Threshold:N2} ({Pct}% of {Bankroll:N2}) — EMERGENCY HALT",
                loss, threshold, HaltRatio * 100, bankroll);
            return SystemStatus.EMERGENCY_HALT;
        }

        return SystemStatus.NORMAL;
    }
}
