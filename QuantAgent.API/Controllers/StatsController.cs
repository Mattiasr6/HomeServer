using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly QuantDbContext _context;
    private readonly IConfiguration _configuration;

    public StatsController(QuantDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    /// <summary>
    /// Returns the key betting dashboard metrics:
    /// Win Rate, Yield, and Monthly ROI.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardStatsDto>> GetDashboard()
    {
        // --- Resolved bets ---
        var resolved = await _context.Predicciones
            .Where(p => p.Estado == EstadoPrediccion.Ganada
                     || p.Estado == EstadoPrediccion.Perdida)
            .ToListAsync();

        var wins = resolved.Count(p => p.Estado == EstadoPrediccion.Ganada);
        var losses = resolved.Count(p => p.Estado == EstadoPrediccion.Perdida);
        var totalResolved = wins + losses;

        // Win Rate = (Ganadas / (Ganadas + Perdidas)) * 100
        var winRate = totalResolved > 0
            ? Math.Round((double)wins / totalResolved * 100, 2)
            : 0.0;

        // Net profit in units (stake = 1 unit per bet)
        // For wins: profit = cuota - 1 (net of stake)
        // For losses: profit = -1 (stake lost)
        var netProfit = resolved.Sum(p => p.Estado == EstadoPrediccion.Ganada
            ? (double)(p.Cuota - 1m)
            : -1.0);
        netProfit = Math.Round(netProfit, 2);

        // Total stake volume = number of resolved bets * 1 unit each
        var totalStaked = (double)totalResolved;

        // Yield (%) = (NetProfit / TotalStaked) * 100
        var yieldPct = totalStaked > 0
            ? Math.Round(netProfit / totalStaked * 100, 2)
            : 0.0;

        // Initial bankroll from configuration (default: 1000)
        var initialBankroll = _configuration.GetValue<double>("Bankroll:Total", 1000.0);

        // ROI (%) = (NetProfit / InitialBankroll) * 100
        var roi = Math.Round(netProfit / initialBankroll * 100, 2);

        // Average odds
        var avgOdds = totalResolved > 0
            ? Math.Round(resolved.Average(p => (double)p.Cuota), 3)
            : 0.0;

        // Total predictions (including pending)
        var totalPredictions = await _context.Predicciones.CountAsync();
        var pendingBets = totalPredictions - totalResolved;

        return Ok(new DashboardStatsDto
        {
            TotalPredictions = totalPredictions,
            ResolvedBets = totalResolved,
            PendingBets = pendingBets,
            Wins = wins,
            Losses = losses,
            WinRate = winRate,
            Yield = yieldPct,
            NetProfitUnits = netProfit,
            MonthlyRoi = roi,
            AverageOdds = avgOdds,
            InitialBankroll = initialBankroll,
        });
    }
}

public class DashboardStatsDto
{
    public int TotalPredictions { get; set; }
    public int ResolvedBets { get; set; }
    public int PendingBets { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public double Yield { get; set; }
    public double NetProfitUnits { get; set; }
    public double MonthlyRoi { get; set; }
    public double AverageOdds { get; set; }
    public double InitialBankroll { get; set; }
}
