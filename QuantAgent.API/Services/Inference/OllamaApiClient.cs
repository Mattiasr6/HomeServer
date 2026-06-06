using System.Net.Http;

namespace QuantAgent.API.Services.Inference;

/// <summary>
/// Typed HTTP client for the local Ollama service
/// (<see href="https://ollama.com"/>). The base address is wired
/// at registration time in <c>Program.cs</c> from configuration.
/// </summary>
public class OllamaApiClient
{
    private readonly HttpClient _httpClient;

    public OllamaApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Underlying <see cref="HttpClient"/> with the configured
    /// <c>BaseAddress</c> (e.g. <c>http://127.0.0.1:11434/</c>) and
    /// timeout. Use this directly for streaming or low-level control;
    /// most callers should go through <see cref="OllamaInferenceService"/>.
    /// </summary>
    public HttpClient HttpClient => _httpClient;
}
