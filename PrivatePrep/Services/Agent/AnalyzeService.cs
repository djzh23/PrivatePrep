using System.Text.RegularExpressions;
using PrivatePrep.Services.FactGate;
using PrivatePrep.Services.Privacy;
using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Services.Agent;

public sealed partial class AnalyzeService(
    ISkillGapService skillGap,
    IFactGateService factGate,
    ILlmRouter llm,
    IPiiScrubberService piiScrubber,
    ILogger<AnalyzeService> logger) : IAnalyzeService
{
    public const int MinimumJobDescriptionLength = 100;
    public const int MaxJobDescriptionLength = 12_000;
    public const int MaxCvChars = 50_000;
    public const decimal CultureFailDimensionCap = 2.0m;
    public const decimal CultureFailGlobalCap = 3.5m;

    public async Task<AnalyzeReport> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jd = request.JobDescription?.Trim() ?? "";
        if (jd.Length < MinimumJobDescriptionLength)
            throw new AnalyzeException("jd_too_short", "Die Stellenanzeige ist zu kurz.");
        if (jd.Length > MaxJobDescriptionLength)
            jd = jd[..MaxJobDescriptionLength];

        var cv = (request.CvText ?? "").Trim();
        if (cv.Length == 0)
            throw new AnalyzeException("profile_incomplete", "Bitte Profil vervollständigen");
        if (cv.Length > MaxCvChars)
            cv = cv[..MaxCvChars];

        cv = piiScrubber.ScrubBestEffort(cv);

        var story = request.StoryText?.Trim() ?? "";
        var gap = skillGap.Classify(cv, jd);

        var systemPrompt = AnalyzePromptTemplate.Fill(gap);
        var userMessage = AnalyzePromptTemplate.BuildUserMessage(cv, story, jd);

        var first = await llm.CompleteAsync(systemPrompt, userMessage, ct).ConfigureAwait(false);
        var parsed = TryParse(first.Content);
        var used = first;

        if (parsed is null)
        {
            logger.LogWarning("Analyze JSON parse failed on first Groq response. UserId {UserId}", request.UserId);
            var retryMessage = userMessage
                + "\n\nYour previous response was not valid JSON. Return ONLY the JSON object, no prose.";
            var second = await llm.CompleteAsync(systemPrompt, retryMessage, ct).ConfigureAwait(false);
            parsed = TryParse(second.Content);
            used = second;
            if (parsed is null)
                throw new AnalyzeException("llm_parse_failed", "The model did not return valid JSON.");
        }

        var warnings = parsed.Warnings.Select(PlainGerman).ToList();
        var roleSummary = PlainGerman(parsed.RoleSummary);
        var cultureScreen = NormalizeCultureScreen(parsed.CultureScreen);
        var global = ClampScore(parsed.GlobalScore);
        var culture = ClampScore(parsed.Culture);

        // V2 fields. All optional: a V1-shaped LLM response (or a retry that dropped them) leaves
        // these null/empty rather than failing the whole analysis.
        var verdictHeadline = NullIfBlank(parsed.VerdictHeadline) is { } vh ? PlainGerman(vh) : null;
        var verdictParagraph = NullIfBlank(parsed.VerdictParagraph) is { } vp ? PlainGerman(vp) : null;
        var dimensionReasons = parsed.DimensionReasons is null
            ? null
            : new DimensionReasons(
                PlainGerman(parsed.DimensionReasons.CvMatch),
                PlainGerman(parsed.DimensionReasons.RoleAlignment),
                PlainGerman(parsed.DimensionReasons.Culture),
                PlainGerman(parsed.DimensionReasons.RedFlags));
        var sectionFindings = parsed.SectionFindings
            .Where(f => !string.IsNullOrWhiteSpace(f.Section) && !string.IsNullOrWhiteSpace(f.Observation))
            .Select(f => new SectionFinding(f.Section, PlainGerman(f.Label), PlainGerman(f.Observation), PlainGerman(f.Action)))
            .ToList();
        var actionPlan = parsed.ActionPlan
            .Where(a => !string.IsNullOrWhiteSpace(a.Action))
            .OrderBy(a => a.Priority)
            .Select(a => new ActionPlanItem(a.Priority, PlainGerman(a.Action), a.EffortMinutes, NormalizeImpact(a.Impact)))
            .ToList();

        if (cultureScreen == "fail")
        {
            if (culture > CultureFailDimensionCap)
                culture = CultureFailDimensionCap;
            if (global > CultureFailGlobalCap)
            {
                global = CultureFailGlobalCap;
                warnings.Add("Culture-Cap: globaler Score auf 3.5 begrenzt, Culture-Dimension auf 2/5.");
            }
            else if (!warnings.Exists(w => w.Contains("Culture-Cap", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("Culture-Screen ist fail: Culture-Dimension auf 2/5 begrenzt.");
            }
        }

        var bullets = parsed.Bullets
            .Where(b => !string.IsNullOrWhiteSpace(b.OriginalBullet) && !string.IsNullOrWhiteSpace(b.RewrittenBullet))
            .Take(5)
            .Select(b => b with { Reasoning = PlainGerman(b.Reasoning) })
            .ToList();

        // V2 narrative fields are additional LLM claims about the candidate, so they go through the
        // same FactGate check as bullets: an invented skill in verdict_paragraph or a section
        // finding is exactly the failure mode FactGate exists to catch.
        var generatedText = string.Join(
            "\n",
            [
                roleSummary,
                verdictHeadline ?? "",
                verdictParagraph ?? "",
                .. warnings,
                .. DimensionReasonSentences(dimensionReasons),
                .. sectionFindings.SelectMany(f => new[] { f.Observation, f.Action }),
                .. actionPlan.Select(a => a.Action),
                .. bullets.Select(b => $"{b.OriginalBullet}\n{b.RewrittenBullet}\n{b.Reasoning}"),
            ]);

        var allowed = gap.Existing.Concat(gap.SupportedByResume).ToList();
        var factResult = factGate.Verify(
            generatedText,
            new FactGateContext(cv, story, allowed));

        if (!factResult.Passed)
        {
            logger.LogInformation(
                "FactGate blocked analyze output. UserId {UserId} Violations {Count}",
                request.UserId, factResult.Violations.Count);
            warnings.Add("FactGate hat den generierten Text blockiert. Umformulierungen werden nicht angezeigt.");
            return new AnalyzeReport(
                global,
                new ScoreDimensions(ClampScore(parsed.CvMatch), ClampScore(parsed.RoleAlignment), culture, ClampScore(parsed.RedFlags)),
                gap,
                [],
                "",
                warnings,
                cultureScreen,
                factResult.Violations,
                used.ModelUsed,
                used.InputTokens,
                used.OutputTokens,
                UnverifiedBullets: bullets);
        }

        return new AnalyzeReport(
            global,
            new ScoreDimensions(ClampScore(parsed.CvMatch), ClampScore(parsed.RoleAlignment), culture, ClampScore(parsed.RedFlags)),
            gap,
            bullets,
            roleSummary.Trim(),
            warnings,
            cultureScreen,
            [],
            used.ModelUsed,
            used.InputTokens,
            used.OutputTokens,
            DimensionReasons: dimensionReasons,
            VerdictHeadline: verdictHeadline,
            VerdictParagraph: verdictParagraph,
            SectionFindings: sectionFindings,
            ActionPlan: actionPlan);
    }

    private static IEnumerable<string> DimensionReasonSentences(DimensionReasons? reasons)
    {
        if (reasons is null)
            yield break;
        yield return reasons.CvMatch;
        yield return reasons.RoleAlignment;
        yield return reasons.Culture;
        yield return reasons.RedFlags;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string NormalizeImpact(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "high" or "medium" or "low" => value,
            _ => "medium",
        };
    }

    // Users do not know the abbreviation, and the model uses it despite the prompt rule.
    private static string PlainGerman(string text) =>
        JobDescriptionAbbreviation().Replace(text, m => m.Value.EndsWith('s') ? "Stellenanzeigen" : "Stellenanzeige");

    [GeneratedRegex(@"\bJDs?\b")]
    private static partial Regex JobDescriptionAbbreviation();

    private static AnalyzeJsonParser.LlmAnalyzePayload? TryParse(string content) =>
        AnalyzeJsonParser.TryParse(content, out var payload) ? payload : null;

    private static string NormalizeCultureScreen(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "pass" or "caution" or "fail" or "not_evaluated" => value,
            _ => "not_evaluated",
        };
    }

    private static decimal ClampScore(decimal value)
    {
        if (value < 1.0m) return 1.0m;
        if (value > 5.0m) return 5.0m;
        return decimal.Round(value, 1);
    }
}
