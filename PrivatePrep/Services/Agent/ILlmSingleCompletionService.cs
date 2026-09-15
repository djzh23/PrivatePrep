namespace PrivatePrep.Services.Agent;

/// <summary>
/// Ein einzelner, nicht-streamender LLM-Aufruf (z. B. strukturierte CV-Extraktion).
/// Entkoppelt Controller wie <see cref="Controllers.ProfileController"/> von <see cref="AgentService"/>.
/// </summary>
public interface ILlmSingleCompletionService
{
    /// <summary>Delegiert an Groq mit internem Token-Deckel (max. 800).</summary>
    Task<string> CompleteAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default);
}
