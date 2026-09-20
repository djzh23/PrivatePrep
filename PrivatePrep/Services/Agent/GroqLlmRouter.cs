using PrivatePrep.Models;
using PrivatePrep.Services.Groq;

namespace PrivatePrep.Services.Agent;

/// <summary>
/// Groq-only completion router. Gemini/Anthropic fallbacks are V2 (Blueprint 00).
/// TODO: Groq prompt caching (cache_control) when the API exposes it for this model.
/// </summary>
public sealed class GroqLlmRouter(GroqChatCompletionService groq, ILogger<GroqLlmRouter> logger) : ILlmRouter
{
    public const int HardTimeoutSeconds = 30;
    public const int AnalyzeMaxTokens = 3500;

    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        if (!groq.IsConfigured)
            throw new AnalyzeException("llm_unavailable", "Groq is not configured.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(HardTimeoutSeconds));

        var sampling = new GroqSamplingOptions(Temperature: 0.4, FrequencyPenalty: 0.3, PresencePenalty: 0.1);
        var messages = new List<GroqChatMessage> { new() { Role = "user", Content = userMessage } };

        GroqCompletionResult result;
        try
        {
            result = await groq
                .CompleteAsync(systemPrompt, messages, AnalyzeMaxTokens, sampling, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Groq analyze call timed out after {Seconds}s", HardTimeoutSeconds);
            throw new AnalyzeException("llm_unavailable", "The language model timed out. Please retry.");
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
        {
            var error = result.Error ?? "Groq request failed.";
            logger.LogWarning("Groq analyze call failed: {StatusHint}", TruncateLog(error, 160));
            if (error.Contains("429", StringComparison.Ordinal))
                throw new AnalyzeException("llm_unavailable", "The language model is rate-limited. Please retry shortly.");
            throw new AnalyzeException("llm_unavailable", "The language model is temporarily unavailable. Please retry.");
        }

        return new LlmResponse(result.Content.Trim(), result.Model, result.InputTokens, result.OutputTokens);
    }

    private static string TruncateLog(string value, int max)
    {
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
