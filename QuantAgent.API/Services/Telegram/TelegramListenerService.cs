using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using QuantAgent.API.Services.Scraping;
using QuantAgent.API.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Jobs;
using Hangfire;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services;

namespace QuantAgent.API.Services.Telegram;

/// <summary>
/// Long-running hosted service that polls Telegram for incoming
/// updates via <see cref="ITelegramBotClient.ReceiveAsync"/>.
/// The actual command parsing (e.g. <c>/status</c>, <c>/stats</c>)
/// will be wired in once the core domain exposes query endpoints;
/// for now every text message is logged so we can confirm the
/// listener is alive in dev.
/// </summary>
public class TelegramListenerService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramListenerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public TelegramListenerService(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramListenerService> logger)
    {
        _bot = bot;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            // Receive every update type. Tighten this list once we
            // know exactly which events we care about.
            AllowedUpdates = Array.Empty<UpdateType>(),
            // Drop updates that piled up while the bot was offline
            // so we don't replay stale history on each restart.
            DropPendingUpdates = true
        };

        _logger.LogInformation("Telegram listener starting (DropPendingUpdates=true)");

        try
        {
            await _bot.ReceiveAsync(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandleErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path when the host stops.
            _logger.LogInformation("Telegram listener stopped");
        }
        catch (Exception ex)
        {
            // Last-resort guard: any unexpected exception here would
            // otherwise bring the whole host down.
            _logger.LogCritical(ex, "Telegram listener crashed");
        }
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        // Route callback queries from inline feedback buttons (Order #32)
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callback)
        {
            await HandleFeedbackCallbackAsync(botClient, callback, cancellationToken);
            return;
        }

        if (update.Type != UpdateType.Message || update.Message is not { } message)
            return;

        var from = message.From?.Username ?? message.From?.Id.ToString() ?? "unknown";
        var text = message.Text ?? string.Empty;
        _logger.LogInformation("Telegram message from {From} in chat {ChatId}: {Text}",
            from, message.Chat.Id, text);

        // Route commands
        if (text.StartsWith("/stats ", StringComparison.OrdinalIgnoreCase))
        {
            var teamName = text["/stats ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(teamName))
                await HandleStatsCommandAsync(botClient, message.Chat.Id, teamName, cancellationToken);
            return;
        }
        if (text.StartsWith("/reglas ", StringComparison.OrdinalIgnoreCase))
        {
            var teamName = text["/reglas ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(teamName))
                await HandleReglasCommandAsync(botClient, message.Chat.Id, teamName, cancellationToken);
            return;
        }
        if (text.StartsWith("/analizar ", StringComparison.OrdinalIgnoreCase))
        {
            var fixtureIdStr = text["/analizar ".Length..].Trim();
            if (int.TryParse(fixtureIdStr, out var fixtureId) && fixtureId > 0)
                await HandleAnalizarCommandAsync(botClient, message.Chat.Id, fixtureId, cancellationToken);
            else
                await botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Uso: /analizar <fixtureId>  (ej. /analizar 123456)",
                    cancellationToken: cancellationToken);
            return;
        }
        if (text.StartsWith("/buscar ", StringComparison.OrdinalIgnoreCase))
        {
            var teamName = text["/buscar ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(teamName))
                await HandleBuscarCommandAsync(botClient, message.Chat.Id, teamName, cancellationToken);
            else
                await botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Uso: /buscar <nombre del equipo>  (ej. /buscar Brasil)",
                    cancellationToken: cancellationToken);
            return;
        }
        if (string.Equals(text, "/ingestar_hoy", StringComparison.OrdinalIgnoreCase))
        {
            BackgroundJob.Enqueue<MatchIngestionJob>(x => x.ExecuteAsync(default));
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "Modo cazador activado. Buscando partidos en las ligas activas...",
                cancellationToken: cancellationToken);
            return;
        }

        if (string.Equals(text, "/emergency_stop", StringComparison.OrdinalIgnoreCase))
        {
            using var haltScope = _scopeFactory.CreateScope();
            var safetyValve = haltScope.ServiceProvider.GetRequiredService<ISafetyValveService>();
            safetyValve.SetManualHalt(true);
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "🚨 Parada de emergencia activada. El sistema no generará nuevas predicciones. Usa /resume para reanudar.",
                cancellationToken: cancellationToken);
            _logger.LogWarning("[/emergency_stop] Manual halt activated by Telegram user {From}", from);
            return;
        }

        if (string.Equals(text, "/resume", StringComparison.OrdinalIgnoreCase))
        {
            using var resumeScope = _scopeFactory.CreateScope();
            var safetyValveResume = resumeScope.ServiceProvider.GetRequiredService<ISafetyValveService>();
            safetyValveResume.SetManualHalt(false);
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "✅ Sistema reanudado. El sistema continuará generando predicciones normalmente.",
                cancellationToken: cancellationToken);
            _logger.LogInformation("[/resume] Manual halt released by Telegram user {From}", from);
            return;
        }
        // Unknown command — log and ignore
        _logger.LogInformation("Unrecognized command from {From}: {Text}", from, text);
    }
    private async Task HandleStatsCommandAsync(
        ITelegramBotClient botClient,
        long chatId,
        string teamName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scraper = scope.ServiceProvider.GetRequiredService<ISoccerStatsScraperService>();

            string[] leagues = ["spain", "england", "germany", "italy", "france"];

            foreach (var league in leagues)
            {
                var stats = await scraper.GetTeamStatsAsync(teamName, league, cancellationToken);
                if (stats is not null)
                {
                    var response = FormatStatsMessage(teamName, stats);
                    await botClient.SendMessage(
                        chatId: chatId,
                        text: response,
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);
                    _logger.LogInformation(
                        "Sent /stats response for '{Team}' in league '{League}'",
                        teamName, league);
                    return;
                }
            }

            // Not found in any league
            var notFound = $"\u274C No pude encontrar estad\u00EDsticas para '<b>{teamName}</b>'.";
            await botClient.SendMessage(
                chatId: chatId,
                text: notFound,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing /stats command for '{Team}'", teamName);
            try
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "\u274C Ocurri\u00F3 un error al buscar estad\u00EDsticas. Intenta de nuevo m\u00E1s tarde.",
                    cancellationToken: cancellationToken);
            }
            catch (Exception exInner)
            {
                _logger.LogWarning(exInner, "Failed to send error notification to chat {ChatId}", chatId);
            }
        }
    }

    private async Task HandleReglasCommandAsync(
        ITelegramBotClient botClient,
        long chatId,
        string teamName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

            var rules = await db.ReglasAprendidas
                .Where(r => r.Equipo == teamName)
                .OrderByDescending(r => r.Peso)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken);

            if (rules.Count == 0)
            {
                var msg = $"\u2705 Memoria limpia. No hay reglas de autocr\u00EDtica para '<b>{teamName}</b>'.";
                await botClient.SendMessage(chatId: chatId, text: msg, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"<b>\U0001F9E0 Memoria Cuantitativa: {teamName}</b>");
            for (int i = 0; i < rules.Count; i++)
            {
                sb.AppendLine($"{i + 1}. [Peso: {rules[i].Peso}] {rules[i].Regla}");
            }

            await botClient.SendMessage(chatId: chatId, text: sb.ToString(), parseMode: ParseMode.Html, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing /reglas command for '{Team}'", teamName);
            try
            {
                await botClient.SendMessage(chatId: chatId, text: "\u274C Ocurri\u00F3 un error al consultar la memoria.", cancellationToken: cancellationToken);
            }
            catch (Exception exInner)
            {
                _logger.LogWarning(exInner, "Failed to send reglas error notification to chat {ChatId}", chatId);
            }
        }
    }

    private async Task HandleAnalizarCommandAsync(
        ITelegramBotClient botClient,
        long chatId,
        int fixtureId,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var footballApi = scope.ServiceProvider.GetRequiredService<FootballApiService>();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        try
        {
            _logger.LogInformation("[/analizar] Fetching fixture {FixtureId}", fixtureId);

            var dto = await footballApi.GetFixtureByIdAsync(fixtureId);

            // Idempotency: skip if already in DB
            var exists = await db.Partidos.AnyAsync(p => p.FixtureId == dto.FixtureId, cancellationToken);
            if (exists)
            {
                var msg = $"El fixture {fixtureId} ({dto.EquipoLocal} vs {dto.EquipoVisitante}) ya existe en la base de datos.";
                await botClient.SendMessage(chatId: chatId, text: msg, cancellationToken: cancellationToken);
                return;
            }

            var partido = new Partido
            {
                FixtureId = dto.FixtureId,
                EquipoLocal = dto.EquipoLocal,
                EquipoVisitante = dto.EquipoVisitante,
                FechaInicio = dto.FechaInicio,
                Estado = EstadoPartido.Pendiente
            };

            db.Partidos.Add(partido);
            await db.SaveChangesAsync(cancellationToken);

            // Schedule Phase B verification 130 min after kick-off
            var scheduledAt = dto.FechaInicio.AddMinutes(130);
            BackgroundJob.Schedule<PostMatchVerificationJob>(
                x => x.VerifyMatchAsync(partido.Id, default), scheduledAt);

            // Enqueue Phase C analysis immediately
            BackgroundJob.Enqueue<ValueBetDetectionJob>(
                x => x.AnalyzeSingleMatchAsync(partido.Id, CancellationToken.None));

            var confirmMsg = $"OK Partido {dto.EquipoLocal} vs {dto.EquipoVisitante} inyectado. Analizando cuotas y estadisticas...";
            await botClient.SendMessage(
                chatId: chatId,
                text: confirmMsg,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "[/analizar] Done: fixture {FixtureId} -> {Local} vs {Visitante} (PartidoId={Id})",
                fixtureId, dto.EquipoLocal, dto.EquipoVisitante, partido.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[/analizar] Failed for fixture {FixtureId}", fixtureId);
            var errorMsg = $"Error al procesar el fixture {fixtureId}: {ex.Message}";
            try
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: errorMsg,
                    cancellationToken: cancellationToken);
            }
            catch (Exception exInner)
            {
                _logger.LogWarning(exInner, "Failed to send analizar error notification to chat {ChatId}", chatId);
            }
        }
    }

    private async Task HandleBuscarCommandAsync(
        ITelegramBotClient botClient,
        long chatId,
        string teamName,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var footballApi = scope.ServiceProvider.GetRequiredService<FootballApiService>();

        try
        {
            _logger.LogInformation("[/buscar] Searching for '{Team}'", teamName);

            var todayFixtures = await footballApi.GetAllFixturesTodayAsync();

            var lowerName = teamName.ToLowerInvariant();
            var matches = todayFixtures
                .Where(p => p.EquipoLocal.ToLowerInvariant().Contains(lowerName)
                         || p.EquipoVisitante.ToLowerInvariant().Contains(lowerName))
                .ToList();

            if (matches.Count == 0)
            {
                var noResults = $"No se encontraron partidos hoy para '{teamName}'.";
                await botClient.SendMessage(
                    chatId: chatId,
                    text: noResults,
                    cancellationToken: cancellationToken);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Resultados para '{teamName}':");
            sb.AppendLine();
            foreach (var m in matches.Take(20)) // cap display at 20
            {
                sb.AppendLine($"  [ID: {m.FixtureId}] {m.EquipoLocal} vs {m.EquipoVisitante} ({m.FechaInicio:HH:mm} UTC)");
            }
            if (matches.Count > 20)
                sb.AppendLine($"  ... y {matches.Count - 20} resultado(s) mas");

            await botClient.SendMessage(
                chatId: chatId,
                text: sb.ToString(),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "[/buscar] Found {Count} matches for '{Team}'",
                matches.Count, teamName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[/buscar] Failed for '{Team}'", teamName);
            try
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: $"Error al buscar '{teamName}': {ex.Message}",
                    cancellationToken: cancellationToken);
            }
            catch (Exception exInner)
            {
                _logger.LogWarning(exInner, "Failed to send buscar error notification to chat {ChatId}", chatId);
            }
        }
    }

    private static string FormatStatsMessage(string teamName, TeamStatsDto stats)
    {
        return $"<b>\U0001F4CA Estad\u00EDsticas de {teamName}</b>\n" +
               $"\U0001F3C6 Posici\u00F3n: {stats.Posicion} | Puntos: {stats.Puntos}\n" +
               $"\u26BD Goles: {stats.GolesFavor} GF / {stats.GolesContra} GC\n" +
               $"\U0001F4C8 % Over 2.5: {stats.Over25}\n" +
               $"\U0001F6A9 Corners: {stats.CornersLocal:F1} (Local) | {stats.CornersVisitante:F1} (Visita)";
    }

    /// <summary>
    /// Handles inline keyboard callbacks from value-bet feedback buttons.
    /// Callback data format: <c>vb_{prediccionId:N}_1</c> (hit) or <c>vb_{prediccionId:N}_0</c> (miss).
    /// Updates the Prediccion.Estado in the database for manual backtesting.
    /// </summary>
    private async Task HandleFeedbackCallbackAsync(
        ITelegramBotClient botClient,
        CallbackQuery callback,
        CancellationToken cancellationToken)
    {
        var data = callback.Data ?? string.Empty;
        _logger.LogInformation("Feedback callback: {Data} from {User}",
            data, callback.From?.Username ?? callback.From?.Id.ToString() ?? "unknown");

        // Expected format: "vb_{guid:n}_1" or "vb_{guid:n}_0"
        if (!data.StartsWith("vb_") || data.Length < 5)
        {
            await botClient.AnswerCallbackQuery(callback.Id,
                text: "Enlace inv\u00E1lido.", cancellationToken: cancellationToken);
            return;
        }

        // Parse: strip "vb_" prefix, split by last '_'
        var underscoreIndex = data.LastIndexOf('_');
        if (underscoreIndex < 4)
        {
            await botClient.AnswerCallbackQuery(callback.Id,
                text: "Formato inv\u00E1lido.", cancellationToken: cancellationToken);
            return;
        }

        var idStr = data[3..underscoreIndex];       // between "vb_" and last "_"
        var resultFlag = data[(underscoreIndex + 1)..]; // "1" or "0"

        if (!Guid.TryParseExact(idStr, "N", out var prediccionId))
        {
            await botClient.AnswerCallbackQuery(callback.Id,
                text: "ID inv\u00E1lido.", cancellationToken: cancellationToken);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

            var prediccion = await db.Predicciones.FindAsync(
                new object[] { prediccionId }, cancellationToken);
            if (prediccion is null)
            {
                await botClient.AnswerCallbackQuery(callback.Id,
                    text: "Predicci\u00F3n no encontrada.", cancellationToken: cancellationToken);
                return;
            }

            var acertada = resultFlag == "1";
            prediccion.Estado = acertada
                ? EstadoPrediccion.Ganada
                : EstadoPrediccion.Perdida;
            await db.SaveChangesAsync(cancellationToken);

            var confirmText = acertada
                ? "\u2705 \u00A1Gracias! Marcada como ACERTADA."
                : "\u274C \u00A1Gracias! Marcada como FALLADA.";

            await botClient.AnswerCallbackQuery(
                callback.Id, text: confirmText,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Feedback processed: Prediccion {Id} -> {Result} (user: {User})",
                prediccionId,
                acertada ? "ACERTADA" : "FALLADA",
                callback.From?.Username ?? callback.From?.Id.ToString() ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing feedback callback for data: {Data}", data);
            try
            {
                await botClient.AnswerCallbackQuery(callback.Id,
                    text: "Error al procesar. Intenta de nuevo.",
                    cancellationToken: cancellationToken);
            }
            catch (Exception exInner)
            {
                _logger.LogWarning(exInner, "Failed to answer feedback callback query for data: {Data}", callback.Data ?? "(null)");
            }
        }
    }

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }
}
