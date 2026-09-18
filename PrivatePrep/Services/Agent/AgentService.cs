using PrivatePrep.Models;
using PrivatePrep.Services.Groq;

namespace PrivatePrep.Services.Agent;

/// <summary>Single-shot Groq completion backing <see cref="ILlmSingleCompletionService"/> (CV parsing).</summary>
public sealed class AgentService(GroqChatCompletionService groqChat)
{
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
