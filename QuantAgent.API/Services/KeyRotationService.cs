using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Services;

/// <summary>
/// Default <see cref="IKeyRotationService"/> backed by the
/// <c>api_keys</c> table. Registered as Singleton; uses
/// <see cref="IServiceScopeFactory"/> to resolve scoped
/// <see cref="QuantDbContext"/> on each operation.
/// </summary>
public class KeyRotationService : IKeyRotationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KeyRotationService> _logger;

    public KeyRotationService(
        IServiceScopeFactory scopeFactory,
        ILogger<KeyRotationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ApiKey> GetValidKeyAsync(string deporte)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        // Prefer the key with the fewest failures among non-limited, non-revoked keys
        var key = await db.ApiKeys
            .Where(k => k.Deporte == deporte
                     && k.Estado == EstadoApiKey.Activa)
            .OrderBy(k => k.Fallos)
            .ThenBy(k => k.UltimoUso) // least recently used as tiebreaker
            .FirstOrDefaultAsync();

        if (key is null)
        {
            _logger.LogWarning(
                "No valid {Deporte} key found — all keys are Limited or Revoked.",
                deporte);
            throw new InvalidOperationException(
                $"No valid API key available for sport '{deporte}'.");
        }

        // Update last-used timestamp
        key.UltimoUso = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogDebug("Using {Deporte} key {KeyId} (failures={Fallos})",
            deporte, key.Id, key.Fallos);

        return key;
    }

    public async Task MarkAsLimitedAsync(Guid keyId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        var key = await db.ApiKeys.FindAsync(keyId);
        if (key is null)
        {
            _logger.LogWarning("Key {KeyId} not found — cannot mark as Limited.", keyId);
            return;
        }

        key.Estado = EstadoApiKey.Limitada;
        key.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogWarning(
            "Key {KeyId} ({Deporte}) marked as Limitada due to rate-limit (429).",
            keyId, key.Deporte);

        // Notify via Telegram that a key was consumed
        _logger.LogInformation(
            "[KEYROTATION] Key {KeyId} ({Deporte}) consumed — {ActiveCount} keys remaining for this sport.",
            keyId, key.Deporte,
            await db.ApiKeys.CountAsync(k => k.Deporte == key.Deporte && k.Estado == EstadoApiKey.Activa));
    }

    public async Task RecordSuccessAsync(Guid keyId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        var key = await db.ApiKeys.FindAsync(keyId);
        if (key is null) return;

        key.Exitos++;
        key.UltimoUso = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RecordFailureAsync(Guid keyId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuantDbContext>();

        var key = await db.ApiKeys.FindAsync(keyId);
        if (key is null) return;

        key.Fallos++;
        key.UltimoUso = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
