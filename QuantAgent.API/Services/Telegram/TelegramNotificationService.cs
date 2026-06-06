using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using QuantAgent.API.Models;
using Telegram.Bot.Types.Enums;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Services.Telegram;

/// <summary>
/// Default <see cref="ITelegramNotificationService"/> implementation
/// that sends alerts to the chat id configured under
/// <c>TelegramBot:ChatIdAdministrador</c>.
/// Registered as Singleton because it is stateless and only
/// delegates to the long-lived <see cref="ITelegramBotClient"/>.
/// </summary>
public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly ITelegramBotClient _bot;
    private readonly string _chatId;
    private readonly ILogger<TelegramNotificationService> _logger;

    private static readonly Dictionary<TipoMercado, string> MarketEmojis = new()
    {
        [TipoMercado.Ganador] = "\U0001F3C6", // 🏆
        [TipoMercado.Corners] = "\U0001F3AF", // 🎯
        [TipoMercado.Goles] = "\u26BD",       // ⚽
    };

    private const string Bet365BaseUrl = "https://www.bet365.com";

    public TelegramNotificationService(
        ITelegramBotClient bot,
        IConfiguration configuration,
        ILogger<TelegramNotificationService> logger)
    {
        _bot = bot;
        _logger = logger;

        var chatId = configuration["TelegramBot:ChatIdAdministrador"];
        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new InvalidOperationException(
                "TelegramBot:ChatIdAdministrador is required for TelegramNotificationService.");
        }
        _chatId = chatId;
    }

    public async Task SendAlertAsync(string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("SendAlertAsync called with empty message — skipping");
            return;
        }

        try
        {
            // Telegram caps outgoing messages at 4096 chars; split if needed.
            const int maxChunk = 4000;
            for (var offset = 0; offset < message.Length; offset += maxChunk)
            {
                var chunk = message.Substring(offset, Math.Min(maxChunk, message.Length - offset));
                await _bot.SendMessage(
                    chatId: _chatId,
                    text: chunk,
                    cancellationToken: ct);
            }

            _logger.LogInformation("Telegram alert sent ({Length} chars) to chat {ChatId}",
                message.Length, _chatId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never let a notification failure break the calling job;
            // log and swallow so the agent keeps running.
            _logger.LogError(ex, "Failed to send Telegram alert to chat {ChatId}", _chatId);
        }
    }

    public async Task SendValueBetAlertAsync(
        Prediccion prediccion, Partido partido, CancellationToken ct = default)
    {
        try
        {
            var emoji = MarketEmojis.GetValueOrDefault(prediccion.Mercado, "\U0001F4A1"); // 💡 fallback

            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"{emoji} *VALOR DETECTADO* {emoji}");
            builder.AppendLine();
            builder.AppendLine($"*{partido.EquipoLocal} vs {partido.EquipoVisitante}*");
            builder.AppendLine();
            builder.AppendLine($"Mercado: {prediccion.Mercado}");
            builder.AppendLine($"Seleccion: {prediccion.Seleccion}");
            builder.AppendLine($"Confianza: {prediccion.Confianza}%");
            builder.AppendLine($"Cuota: {prediccion.Cuota:N2}");
            if (prediccion.StakeSugerido > 0m)
            {
                builder.AppendLine(string.Format(
                    "Stake Sugerido: {0:N2} unidades", prediccion.StakeSugerido));
            }
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(prediccion.Razonamiento))
            {
                var razon = prediccion.Razonamiento.Length > 180
                    ? prediccion.Razonamiento[..180] + "..."
                    : prediccion.Razonamiento;
                builder.AppendLine($"_{razon}_");
                builder.AppendLine();
            }

            // Link to the match on Bet365 (if fixture ID is available)
            if (partido.FixtureId.HasValue)
            {
                builder.AppendLine($"[Ver en Bet365]({Bet365BaseUrl})");
            }
            else
            {
                builder.AppendLine("(sin enlace directo — fixture ID no disponible)");
            }

            // Inline keyboard for manual feedback (backtesting)
            var callbackPrefix = $"vb_{prediccion.Id:n}"; // compact: no hyphens
            var inlineKeyboard = new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData("\u2705 Acert\u00F3", $"{callbackPrefix}_1"),
                    InlineKeyboardButton.WithCallbackData("\u274C Fall\u00F3", $"{callbackPrefix}_0"),
                ],
            ]);

            // Telegram's MarkdownV2 requires escaping special chars; use HTML instead
            await _bot.SendMessage(
                chatId: _chatId,
                text: builder.ToString(),
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard,
                cancellationToken: ct);

            _logger.LogInformation(
                "Value-bet alert sent for {Local} vs {Visitante} [{Mercado}] conf={Conf}% cuota={Cuota:N2}",
                partido.EquipoLocal, partido.EquipoVisitante,
                prediccion.Mercado, prediccion.Confianza, prediccion.Cuota);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send value-bet alert for Prediccion {Id}: {Local} vs {Visitante} [{Mercado}]",
                prediccion.Id, partido.EquipoLocal, partido.EquipoVisitante, prediccion.Mercado);
        }
    }

    public async Task SendDiscrepancyAlertAsync(
        Prediccion prediccion, Partido partido, double diffPct, CancellationToken ct = default)
    {
        try
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("\u26A0 *ALTA DISCREPANCIA DETECTADA* \u26A0");
            builder.AppendLine();
            builder.AppendLine($"*{partido.EquipoLocal} vs {partido.EquipoVisitante}*");
            builder.AppendLine();
            builder.AppendLine($"Mercado: {prediccion.Mercado}");
            builder.AppendLine($"Selecci\u00F3n: {prediccion.Seleccion}");
            builder.AppendLine($"Diferencia: *{diffPct:F1}%* en probabilidad impl\u00EDcita");
            builder.AppendLine($"Confianza: {prediccion.Confianza}%");
            builder.AppendLine($"Cuota: {prediccion.Cuota:N2}");
            if (prediccion.StakeSugerido > 0m)
            {
                builder.AppendLine($"Stake: {prediccion.StakeSugerido:N2} unidades");
            }
            builder.AppendLine();
            var razon = prediccion.Razonamiento?.Length > 150
                ? prediccion.Razonamiento[..150] + "..."
                : prediccion.Razonamiento;
            builder.AppendLine($"_{razon}_");
            builder.AppendLine();
            builder.AppendLine("\u00BFProceder con el stake sugerido?");

            await _bot.SendMessage(
                chatId: _chatId,
                text: builder.ToString(),
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);

            _logger.LogInformation(
                "Discrepancy alert sent for {Local} vs {Visitante} [{Mercado}] diff={Diff:F1}%",
                partido.EquipoLocal, partido.EquipoVisitante,
                prediccion.Mercado, diffPct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send discrepancy alert for {Local} vs {Visitante}",
                partido.EquipoLocal, partido.EquipoVisitante);
        }
    }

    public async Task SendSafetyAlertAsync(string title, string details, CancellationToken ct = default)
    {
        try
        {
            var text = $"⚠ *{title}* ⚠\n\n{details}";
            await _bot.SendMessage(
                chatId: _chatId,
                text: text,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);

            _logger.LogInformation(
                "Safety alert sent: {Title} ({Length} chars) to chat {ChatId}",
                title, text.Length, _chatId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send safety alert '{Title}' to chat {ChatId}",
                title, _chatId);
        }
    }
}
