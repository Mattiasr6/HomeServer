using System.Net;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models.Enums;
using QuantAgent.API.Services;
using Hangfire;

namespace QuantAgent.API.Jobs;

/// <summary>
/// Every 24 hours, attempts to ping the API-Football endpoint with
/// keys currently marked as <see cref="EstadoApiKey.Limitada"/>.
/// If the endpoint responds successfully, the key is returned to
/// <see cref="EstadoApiKey.Activa"/> — the provider's rate-limit
/// window has expired and the key is healthy again.
/// </summary>
public class KeyHealthCheckJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KeyHealthCheckJob> _logger;

    // Shared HttpClient for health-check pings (not the DI-managed one,
    // since that would compete with the main FootballApiService).
    private static readonly HttpClient PingClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        BaseAddress = new Uri("https://v3.football.api-sports.io/"),
    };

    public KeyHealthCheckJob(
        IServiceScopeFactory scopeFactory,
        ILogger<KeyHealthCheckJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[KeyHealthCheck] Starting daily key health check at {Time:o}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        var limitedKeys = await db.ApiKeys
            .Where(k => k.Estado == EstadoApiKey.Limitada)
            .ToListAsync(ct);

        if (limitedKeys.Count == 0)
        {
            _logger.LogInformation("[KeyHealthCheck] No limited keys to check.");
            return;
        }

        var revived = 0;
        foreach (var key in limitedKeys)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/status");
                request.Headers.Add("x-apisports-key", key.Key);
                using var response = await PingClient.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    key.Estado = EstadoApiKey.Activa;
                    key.Fallos = 0; // reset failure counter on successful recovery
                    key.UpdatedAt = DateTime.UtcNow;
                    revived++;

                    _logger.LogInformation(
                        "[KeyHealthCheck] Key {KeyId} ({Deporte}) revived — rate-limit window expired.",
                        key.Id, key.Deporte);
                }
                else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogInformation(
                        "[KeyHealthCheck] Key {KeyId} ({Deporte}) still rate-limited (429). Will retry in 24h.",
                        key.Id, key.Deporte);
                }
                else
                {
                    _logger.LogWarning(
                        "[KeyHealthCheck] Key {KeyId} ({Deporte}) returned {Status} — unexpected.",
                        key.Id, key.Deporte, (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[KeyHealthCheck] Ping failed for key {KeyId} ({Deporte}) — network error, will retry.",
                    key.Id, key.Deporte);
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[KeyHealthCheck] Done. Checked={Checked} Revived={Revived} StillLimited={StillLimited}",
            limitedKeys.Count, revived, limitedKeys.Count - revived);
    }
}
