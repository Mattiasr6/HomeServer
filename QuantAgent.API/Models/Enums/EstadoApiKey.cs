namespace QuantAgent.API.Models.Enums;

/// <summary>
/// Lifecycle state of an API key managed by the key rotation system.
/// </summary>
public enum EstadoApiKey
{
    Activa = 0,
    Limitada = 1,
    Revocada = 2,
}
