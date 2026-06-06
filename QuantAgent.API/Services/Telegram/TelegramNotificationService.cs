using Telegram.Bot;

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
}
