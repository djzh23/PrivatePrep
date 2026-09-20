using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PrivatePrep.Models;

namespace PrivatePrep.Services.Groq;

public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    /// <summary>Also read from env GROQ_API_KEY (mapped in Program.cs).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Also read from env GROQ_MODEL (mapped in Program.cs).</summary>
    public string Model { get; set; } = "openai/gpt-oss-120b";

    public double Temperature { get; set; } = 0.4;
}

public sealed record GroqSamplingOptions(
    double? Temperature,
    double FrequencyPenalty,
    double PresencePenalty);

/// <summary>Groq OpenAI-compatible chat completions (primary LLM when configured).</summary>
public sealed class GroqChatCompletionService
{
    private readonly HttpClient _http;
    private readonly GroqOptions _opt;
    private readonly ILogger<GroqChatCompletionService> _logger;

    public GroqChatCompletionService(
        HttpClient http,
        IOptions<GroqOptions> options,
        ILogger<GroqChatCompletionService> logger)
    {
        _http = http;
        _opt = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_opt.ApiKey);

    public async Task<GroqCompletionResult> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<GroqChatMessage> messages,
        int maxTokens,
        GroqSamplingOptions? sampling = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GroqCompletionResult
            {
                Success = false,
                Error = "Groq API key is not configured.",
            };
        }

        var model = string.IsNullOrWhiteSpace(_opt.Model) ? "openai/gpt-oss-120b" : _opt.Model.Trim();

        var payloadMessages = new List<Dictionary<string, object>>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            payloadMessages.Add(new Dictionary<string, object> { ["role"] = "system", ["content"] = systemPrompt });

        foreach (var m in messages)
            payloadMessages.Add(new Dictionary<string, object> { ["role"] = m.Role, ["content"] = m.Content });

        var temperature = sampling?.Temperature ?? _opt.Temperature;
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = payloadMessages,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
        };

        body["frequency_penalty"] = sampling?.FrequencyPenalty ?? 0.3;
        body["presence_penalty"] = sampling?.PresencePenalty ?? 0.1;

        // Disable chain-of-thought for Qwen3 models to avoid <think> tokens and English-language drift
        if (model.Contains("qwen", StringComparison.OrdinalIgnoreCase))
            body["reasoning_effort"] = "none";

        // gpt-oss models don't support "none"; reasoning tokens still count against max_tokens, so
        // an unbounded effort can exhaust the budget before any visible content is produced.
        if (model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
            body["reasoning_effort"] = "low";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey.Trim());

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var summary = SummarizeGroqError(raw, (int)resp.StatusCode);
                _logger.LogWarning("Groq API error {Status}: {Summary}", (int)resp.StatusCode, summary);
                return new GroqCompletionResult
                {
                    Success = false,
                    Model = model,
                    Error = $"Groq HTTP {(int)resp.StatusCode}: {summary}",
                };
            }

            var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var content = StripThinkingTags(
                root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "");

            var inTok = 0;
            var outTok = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) inTok = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct)) outTok = ct.GetInt32();
            }

            return new GroqCompletionResult
            {
                Success = !string.IsNullOrWhiteSpace(content),
                Content = content,
                Model = model,
                InputTokens = inTok,
                OutputTokens = outTok,
                Error = string.IsNullOrWhiteSpace(content) ? "Groq returned empty content." : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Groq request failed");
            return new GroqCompletionResult
            {
                Success = false,
                Model = model,
                Error = $"Groq exception: {ex.Message}",
            };
        }
    }

    private static string SummarizeGroqError(string raw, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                if (err.TryGetProperty("message", out var msg))
                {
                    var m = (msg.GetString() ?? string.Empty).Trim();
                    if (m.Length > 160)
                        m = m[..160];
                    if (m.Length > 0)
                        return m;
                }
            }
        }
        catch (JsonException)
        {
            // Response is not JSON; never log the raw body (it can echo prompt/CV text).
        }

        return $"HTTP {status}";
    }

    private static readonly Regex ThinkTagRegex =
        new(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string StripThinkingTags(string content) =>
        ThinkTagRegex.Replace(content, string.Empty).Trim();
}
