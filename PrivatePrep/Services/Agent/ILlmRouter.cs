namespace PrivatePrep.Services.Agent;

public interface ILlmRouter
{
    Task<LlmResponse> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct);
}

public record LlmResponse(string Content, string ModelUsed, int InputTokens, int OutputTokens);
