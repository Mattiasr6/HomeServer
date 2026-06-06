using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly QuantDbContext _context;

    public AnalyticsController(QuantDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Returns aggregated KPI metrics for the betting model's performance.
    /// </summary>
    [HttpGet("kpis")]
    public async Task<ActionResult<KpiDto>> GetKpis()
    {
        var totalBets = await _context.Predicciones.CountAsync();

        var resolved = await _context.Predicciones
            .Where(p => p.Estado == EstadoPrediccion.Ganada || p.Estado == EstadoPrediccion.Perdida)
            .ToListAsync();

        var wins = resolved.Count(p => p.Estado == EstadoPrediccion.Ganada);
        var losses = resolved.Count(p => p.Estado == EstadoPrediccion.Perdida);

        // WinRate: percentage of resolved bets that won
        var winRate = (wins + losses) > 0
            ? Math.Round((double)wins / (wins + losses) * 100, 2)
            : 0;

        // AverageOdds: average cuota across all predictions
        var promedio = await _context.Predicciones
            .AverageAsync(p => (double?)p.Cuota);
        var averageOdds = promedio.HasValue ? Math.Round(promedio.Value, 3) : 0;

        // NetProfit: + (cuota - 1) for wins, -1 for losses, 0 for pending
        var netProfit = Math.Round(
            resolved.Sum(p => p.Estado == EstadoPrediccion.Ganada
                ? (double)(p.Cuota - 1m)
                : -1.0),
            2);

        var pendingBets = totalBets - resolved.Count;

        return Ok(new KpiDto
        {
            TotalBets = totalBets,
            PendingBets = pendingBets,
            ResolvedBets = resolved.Count,
            Wins = wins,
            Losses = losses,
            WinRate = winRate,
            AverageOdds = averageOdds,
            NetProfit = netProfit,
        });
    }
}

public class KpiDto
{
    public int TotalBets { get; set; }
    public int PendingBets { get; set; }
    public int ResolvedBets { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public double AverageOdds { get; set; }
    public double NetProfit { get; set; }
}
