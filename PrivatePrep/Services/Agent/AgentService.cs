using System.Runtime.CompilerServices;
using PrivatePrep.Models;
using PrivatePrep.Services.Groq;
using PrivatePrep.Services.Infrastructure;

namespace PrivatePrep.Services.Agent;

/// <summary>
/// Stateless Groq completion used until Phase 4 replaces this with AnalyzeService.
/// No chat history, no streaming protocol, no Anthropic fallback.
/// </summary>
public sealed class AgentService(
    GroqChatCompletionService groqChat,
    ILogger<AgentService> logger) : IAgentService
{
    public async Task<AgentResponse> RunAsync(AgentRequest request)
    {
        var toolType = string.IsNullOrWhiteSpace(request.ToolType) ? "jobanalyzer" : request.ToolType.ToLowerInvariant();
        var userMessage = UserInputCleaner.CleanUserInput(request.Message);
        var maxTokens = GroqInferenceParameters.MaxTokensFor(toolType);
        var sampling = GroqInferenceParameters.SamplingFor(toolType);
        var systemPrompt =
            "You are PrivatePrep, an AI job-application assistant for the German market. "
            + "Be factual. Never invent skills, metrics, or experience. "
            + "Keywords get reformulated, never fabricated.";

        var groqMessages = new List<GroqChatMessage> { new() { Role = "user", Content = userMessage } };
        var groqResult = await groqChat
            .CompleteAsync(systemPrompt, groqMessages, maxTokens, sampling)
            .ConfigureAwait(false);

        if (!groqResult.Success || string.IsNullOrWhiteSpace(groqResult.Content))
        {
            logger.LogWarning("Groq completion failed. ToolType {ToolType} Error {Error}", toolType, groqResult.Error);
            throw new InvalidOperationException(groqResult.Error ?? "Groq request failed.");
        }

        return new AgentResponse(
            groqResult.Content.Trim(),
            ToolUsed: toolType,
            InputTokens: groqResult.InputTokens,
            OutputTokens: groqResult.OutputTokens,
            Model: groqResult.Model);
    }

    public async IAsyncEnumerable<AgentStreamChunk> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var response = await RunAsync(request).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Reply))
            yield return AgentStreamChunk.TextPart(response.Reply);
        yield return AgentStreamChunk.Done(
            toolUsed: response.ToolUsed,
            inputTokens: response.InputTokens,
            outputTokens: response.OutputTokens,
            model: response.Model);
    }

    public async Task<string> SingleCompletion(string prompt, int maxTokens = 600)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var capped = Math.Min(int.Clamp(maxTokens, 1, 800), 800);
        var groqMessages = new List<GroqChatMessage> { new() { Role = "user", Content = prompt } };
        var sampling = new GroqSamplingOptions(Temperature: 0.1, FrequencyPenalty: 0.1, PresencePenalty: 0.05);

        if (!groqChat.IsConfigured)
            throw new InvalidOperationException("Groq is not configured.");

        var groqResult = await groqChat
            .CompleteAsync(string.Empty, groqMessages, capped, sampling)
            .ConfigureAwait(false);

        if (!groqResult.Success || string.IsNullOrWhiteSpace(groqResult.Content))
            throw new InvalidOperationException(groqResult.Error ?? "Groq request failed.");

        return groqResult.Content.Trim();
    }
}
