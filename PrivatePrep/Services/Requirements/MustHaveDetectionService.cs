using PrivatePrep.Services.Agent;

namespace PrivatePrep.Services.Requirements;

/// <param name="Verified">Must-haves that survived code verification (spec 001, AC-1 decision A).</param>
/// <param name="Rejected">Proposals the model made whose posting quote could not be verified.</param>
/// <param name="ModelUsed">The model that produced <paramref name="Verified"/>, or null if the rule-based fallback ran instead.</param>
/// <param name="UsedFallback">True when step 3a's <see cref="IRequirementsExtractor"/> ran instead of the model.</param>
public sealed record MustHaveDetectionResult(
    IReadOnlyList<VerifiedMustHave> Verified,
    IReadOnlyList<ProposedMustHave> Rejected,
    string? ModelUsed,
    bool UsedFallback);

public interface IMustHaveDetectionService
{
    Task<MustHaveDetectionResult> DetectAsync(string postingText, string cvText, CancellationToken ct);
}

/// <summary>
/// The model proposes must-haves with a verbatim quote and a status vs. the CV; <see cref="IMustHaveVerifier"/>
/// checks the quotes are real. If the model call fails, its JSON cannot be parsed twice in a row, or every
/// single proposal turns out to be hallucinated, this falls back to the rule-based <see cref="IRequirementsExtractor"/>
/// (step 3a) rather than surfacing nothing or an error — a weaker must-have list beats none.
/// </summary>
public sealed class MustHaveDetectionService(
    ILlmRouter llm,
    IMustHaveVerifier verifier,
    IRequirementsExtractor fallbackExtractor,
    ILogger<MustHaveDetectionService> logger) : IMustHaveDetectionService
{
    public async Task<MustHaveDetectionResult> DetectAsync(string postingText, string cvText, CancellationToken ct)
    {
        postingText ??= "";
        cvText ??= "";

        var proposals = await ProposeAsync(postingText, cvText, ct).ConfigureAwait(false);
        if (proposals is null)
            return FallbackResult(postingText);

        var (modelUsed, items) = proposals.Value;
        var result = verifier.Verify(items, postingText, cvText);

        if (result.Verified.Count == 0 && items.Count > 0)
        {
            // Every proposal failed quote verification: this run cannot be trusted at all.
            logger.LogWarning("MustHave: all {Count} proposals failed quote verification; using the rule-based fallback.", items.Count);
            return FallbackResult(postingText);
        }

        return new MustHaveDetectionResult(result.Verified, result.Rejected, modelUsed, UsedFallback: false);
    }

    private async Task<(string ModelUsed, List<ProposedMustHave> Items)?> ProposeAsync(string postingText, string cvText, CancellationToken ct)
    {
        var systemPrompt = MustHavePromptTemplate.Load();
        var userMessage = MustHavePromptTemplate.BuildUserMessage(postingText, cvText);

        LlmResponse first;
        try
        {
            first = await llm.CompleteAsync(systemPrompt, userMessage, ct).ConfigureAwait(false);
        }
        catch (AnalyzeException ex)
        {
            logger.LogWarning("MustHave LLM call failed ({Code}); using the rule-based fallback.", ex.ErrorCode);
            return null;
        }

        if (MustHaveJsonParser.TryParse(first.Content, out var items))
            return (first.ModelUsed, items);

        logger.LogWarning("MustHave JSON parse failed on the first response; retrying once.");
        LlmResponse second;
        try
        {
            var retryMessage = userMessage + "\n\nYour previous response was not valid JSON. Return ONLY the JSON object, no prose.";
            second = await llm.CompleteAsync(systemPrompt, retryMessage, ct).ConfigureAwait(false);
        }
        catch (AnalyzeException ex)
        {
            logger.LogWarning("MustHave LLM retry failed ({Code}); using the rule-based fallback.", ex.ErrorCode);
            return null;
        }

        if (MustHaveJsonParser.TryParse(second.Content, out var retryItems))
            return (second.ModelUsed, retryItems);

        logger.LogWarning("MustHave JSON parse failed twice; using the rule-based fallback.");
        return null;
    }

    private MustHaveDetectionResult FallbackResult(string postingText)
    {
        var extracted = fallbackExtractor.Extract(postingText)
            .Select(r => new VerifiedMustHave(r.Kind, r.Quote, MustHaveStatus.Unclear))
            .ToList();
        return new MustHaveDetectionResult(extracted, [], ModelUsed: null, UsedFallback: true);
    }
}
