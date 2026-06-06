using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Controllers;

[ApiController]
[Route("api/predictions")]
public class PredictionsController : ControllerBase
{
    private readonly QuantDbContext _context;
    private readonly ILogger<PredictionsController> _logger;

    public PredictionsController(QuantDbContext context, ILogger<PredictionsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Returns active predictions (Pendiente) with their match details.
    /// These are the "APOSTAR" recommendations still awaiting verification.
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<PredictionDto>>> GetActive()
    {
        var predictions = await _context.Predicciones
            .Include(p => p.Partido)
            .Where(p => p.Estado == EstadoPrediccion.Pendiente)
            .OrderByDescending(p => p.Confianza)
            .ThenBy(p => p.CreatedAt)
            .Select(p => MapPrediction(p))
            .ToListAsync();

        return Ok(predictions);
    }

    /// <summary>
    /// Returns the full history of resolved predictions (ganadas/perdidas)
    /// with odds and confidence level.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<PredictionDto>>> GetHistory()
    {
        var predictions = await _context.Predicciones
            .Include(p => p.Partido)
            .Where(p => p.Estado == EstadoPrediccion.Ganada || p.Estado == EstadoPrediccion.Perdida)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => MapPrediction(p))
            .ToListAsync();

        return Ok(predictions);
    }

    private static PredictionDto MapPrediction(Prediccion p) => new()
    {
        Id = p.Id,
        PartidoId = p.PartidoId,
        Local = p.Partido?.EquipoLocal ?? "?",
        Visitante = p.Partido?.EquipoVisitante ?? "?",
        Inicio = p.Partido?.FechaInicio ?? DateTime.MinValue,
        MarcadorLocal = p.Partido?.GolesLocal,
        MarcadorVisitante = p.Partido?.GolesVisitante,
        Seleccion = p.Seleccion,
        Cuota = p.Cuota,
        Confianza = p.Confianza,
        Razonamiento = p.Razonamiento,
        Estado = p.Estado.ToString(),
        Creado = p.CreatedAt,
        Actualizado = p.UpdatedAt,
        Mercado = p.Mercado.ToString(),
        CornersOverUnder = p.CornersOverUnder,
        TotalGoals = p.TotalGoals,
    };
}

public class PredictionDto
{
    public Guid Id { get; set; }
    public Guid PartidoId { get; set; }
    public string Local { get; set; } = string.Empty;
    public string Visitante { get; set; } = string.Empty;
    public DateTime Inicio { get; set; }
    public int? MarcadorLocal { get; set; }
    public int? MarcadorVisitante { get; set; }
    public string Seleccion { get; set; } = string.Empty;
    public decimal Cuota { get; set; }
    public int Confianza { get; set; }
    public string Razonamiento { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public DateTime Creado { get; set; }
    public DateTime Actualizado { get; set; }
    public string Mercado { get; set; } = "Ganador";
    public decimal CornersOverUnder { get; set; }
    public decimal TotalGoals { get; set; }
}
