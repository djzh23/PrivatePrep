using PrivatePrep.Services.FactGate;
using PrivatePrep.Services.Privacy;
using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Services.Agent;

public sealed class AnalyzeService(
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
            throw new AnalyzeException("jd_too_short", "JD zu kurz");
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

        var warnings = new List<string>(parsed.Warnings);
        var cultureScreen = NormalizeCultureScreen(parsed.CultureScreen);
        var global = ClampScore(parsed.GlobalScore);
        var culture = ClampScore(parsed.Culture);

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
            .ToList();

        var generatedText = string.Join(
            "\n",
            [parsed.RoleSummary, .. warnings, .. bullets.Select(b => $"{b.OriginalBullet}\n{b.RewrittenBullet}\n{b.Reasoning}")]);

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
                used.OutputTokens);
        }

        return new AnalyzeReport(
            global,
            new ScoreDimensions(ClampScore(parsed.CvMatch), ClampScore(parsed.RoleAlignment), culture, ClampScore(parsed.RedFlags)),
            gap,
            bullets,
            parsed.RoleSummary.Trim(),
            warnings,
            cultureScreen,
            [],
            used.ModelUsed,
            used.InputTokens,
            used.OutputTokens);
    }

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
