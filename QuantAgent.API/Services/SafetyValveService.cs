using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services.Telegram;

namespace QuantAgent.API.Services;

/// <summary>
/// Monitors daily losses and acts as a circuit breaker.
/// When the cumulative lost stake exceeds 5% of bankroll, or
/// when 5+ consecutive losses are detected, the system enters
/// EMERGENCY_HALT — all new predictions and Telegram
/// notifications are suppressed.
/// <para>
/// Registered as Singleton. DB access is scoped via IServiceScopeFactory
/// to avoid captive dependency in the singleton host.
/// </para>
/// </summary>
public class SafetyValveService : ISafetyValveService
{
    private const decimal HaltRatio = 0.05m;
    private const int MaxConsecutiveLosses = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ITelegramNotificationService _telegram;
    private readonly ILogger<SafetyValveService> _logger;

    // Manual halt state (volatile for cross-thread visibility across Hangfire jobs)
    private volatile bool _manuallyHalted;

    // Track last known status to detect NORMAL→HALT and HALT→NORMAL transitions
    private SystemStatus _lastKnownStatus = SystemStatus.NORMAL;
    private readonly object _statusLock = new();

    public bool IsManuallyHalted => _manuallyHalted;

    public SafetyValveService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ITelegramNotificationService telegram,
        ILogger<SafetyValveService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _telegram = telegram;
        _logger = logger;
    }

    public decimal GetBankrollTotal()
    {
        return _configuration.GetValue<decimal>("Bankroll:Total", 1000m);
    }

    public void SetManualHalt(bool halted)
    {
        _manuallyHalted = halted;
        _logger.LogInformation(
            "Manual halt {(Status)} by Telegram command",
            halted ? "ACTIVATED" : "RELEASED");
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

    public async Task<int> GetConsecutiveLossesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        // Fetch most recently resolved predictions (won or lost)
        var recent = await db.Predicciones
            .Where(p => p.Estado == EstadoPrediccion.Ganada
                     || p.Estado == EstadoPrediccion.Perdida)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(MaxConsecutiveLosses * 2) // generous buffer in case of ties
            .ToListAsync(ct);

        var consecutive = 0;
        foreach (var p in recent)
        {
            if (p.Estado == EstadoPrediccion.Perdida)
                consecutive++;
            else
                break; // streak broken by a win
        }

        return consecutive;
    }

    public async Task<SystemStatus> GetSystemStatusAsync(CancellationToken ct = default)
    {
        // 1. Manual halt takes precedence
        if (_manuallyHalted)
        {
            DetectAndNotifyTransition(SystemStatus.EMERGENCY_HALT, 0, 0, 0, ct);
            return SystemStatus.EMERGENCY_HALT;
        }

        // 2. Compute automatic safety checks
        var loss = await GetDailyLossAsync(ct);
        var bankroll = GetBankrollTotal();
        var threshold = bankroll * HaltRatio;
        var consecutiveLosses = await GetConsecutiveLossesAsync(ct);

        var status = (loss > threshold || consecutiveLosses >= MaxConsecutiveLosses)
            ? SystemStatus.EMERGENCY_HALT
            : SystemStatus.NORMAL;

        // 3. Detect transitions and fire Telegram alerts
        DetectAndNotifyTransition(status, loss, threshold, consecutiveLosses, ct);

        return status;
    }

    private void DetectAndNotifyTransition(
        SystemStatus newStatus, decimal loss, decimal threshold, int consecutiveLosses, CancellationToken ct)
    {
        SystemStatus previous;
        lock (_statusLock)
        {
            previous = _lastKnownStatus;
            if (previous == newStatus)
                return; // no transition — nothing to do
            _lastKnownStatus = newStatus;
        }

        if (newStatus == SystemStatus.EMERGENCY_HALT && previous == SystemStatus.NORMAL)
        {
            var bankroll = GetBankrollTotal();
            _logger.LogWarning(
                "SAFETY VALVE: EMERGENCY HALT — loss={Loss:N2} threshold={Threshold:N2} consecutiveLosses={ConsLoss} bankroll={Bankroll:N2}",
                loss, threshold, consecutiveLosses, bankroll);

            // Fire-and-forget Telegram alert (never block the caller on a network send)
            _ = _telegram.SendSafetyAlertAsync(
                "🚨 EMERGENCY HALT",
                $"Pérdida diaria: ${loss:N2} de ${threshold:N2}\n" +
                $"Racha perdedora: {consecutiveLosses} consecutivas\n" +
                $"Bankroll: ${bankroll:N2}\n" +
                $"Usa /resume para reanudar manualmente.",
                ct);
        }
        else if (newStatus == SystemStatus.NORMAL && previous == SystemStatus.EMERGENCY_HALT)
        {
            var bankroll = GetBankrollTotal();
            _logger.LogInformation("SAFETY VALVE: System recovered — returning to NORMAL");

            // Fire-and-forget Telegram alert for recovery notification
            _ = _telegram.SendSafetyAlertAsync(
                "✅ Sistema Recuperado",
                $"El sistema ha vuelto a estado NORMAL.\n" +
                $"Bankroll: ${bankroll:N2}\n" +
                $"Pérdida diaria actual: ${loss:N2}");
        }
    }
}
