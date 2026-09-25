using PrivatePrep.Services.FactGate;
using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Services.Agent;

public interface IAnalyzeService
{
    Task<AnalyzeReport> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct);
}

public record AnalyzeRequest(
    string UserId,
    string CvText,
    string StoryText,
    string JobDescription);

/// <param name="UnverifiedBullets">Set only when FactGate blocked <paramref name="Bullets"/>: the same rewrites
/// the model proposed, unfiltered. The user can ask to see them anyway (never sent pre-selected/auto-shown),
/// with the report making clear they were not checked against the CV.</param>
/// <param name="DimensionReasons">V2. One German sentence per dimension explaining its score. Null when the
/// model did not return it (V1-shaped response) or when FactGate blocked the output.</param>
/// <param name="VerdictHeadline">V2. One-sentence German application recommendation. Null under the same
/// conditions as <paramref name="DimensionReasons"/>.</param>
/// <param name="VerdictParagraph">V2. 2-3 sentence German verdict citing CV/Anzeige content. Null under the
/// same conditions as <paramref name="DimensionReasons"/>.</param>
/// <param name="SectionFindings">V2. Per-CV-section feedback, empty (not null) when the model returned none.</param>
/// <param name="ActionPlan">V2. Priority-ordered next steps, empty (not null) when the model returned none.</param>
public record AnalyzeReport(
    decimal GlobalScore,
    ScoreDimensions Dimensions,
    SkillGapReport SkillGap,
    IReadOnlyList<BulletRewriteSuggestion> Bullets,
    string RoleSummary,
    IReadOnlyList<string> Warnings,
    string CultureScreen,
    IReadOnlyList<FactGateViolation> FactViolations,
    string? ModelUsed = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    IReadOnlyList<BulletRewriteSuggestion>? UnverifiedBullets = null,
    DimensionReasons? DimensionReasons = null,
    string? VerdictHeadline = null,
    string? VerdictParagraph = null,
    IReadOnlyList<SectionFinding>? SectionFindings = null,
    IReadOnlyList<ActionPlanItem>? ActionPlan = null);

public record ScoreDimensions(
    decimal CvMatch,
    decimal RoleAlignment,
    decimal Culture,
    decimal RedFlags);

/// <summary>V2. One German sentence per dimension, explaining why it scored the way it did.</summary>
public record DimensionReasons(
    string CvMatch,
    string RoleAlignment,
    string Culture,
    string RedFlags);

/// <summary>V2. Per-CV-section feedback, emitted only when the CV has material AND the posting has
/// expectations touching that section. <paramref name="Section"/> is one of the canonical IDs
/// (profile, technical_skills, experience, education, certificates, languages, it_kenntnisse, other).</summary>
public record SectionFinding(
    string Section,
    string Label,
    string Observation,
    string Action);

/// <summary>V2. One priority-ordered next step. <paramref name="EffortMinutes"/> is null for ongoing/
/// multi-day tasks (e.g. a language course). <paramref name="Impact"/> is "high", "medium", or "low".</summary>
public record ActionPlanItem(
    int Priority,
    string Action,
    int? EffortMinutes,
    string Impact);

/// <param name="EvidenceLine">V2. The CV line (verbatim or near-verbatim) that justifies the rewrite. Usually
/// equal to <paramref name="OriginalBullet"/>; may differ when the rewrite draws on an adjacent bullet or role
/// header. Null when the model returned a V1-shaped bullet without it.</param>
public record BulletRewriteSuggestion(
    string OriginalBullet,
    string RewrittenBullet,
    string Reasoning,
    string? EvidenceLine = null);

public sealed class AnalyzeException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class AnalyzeRequestDto
{
    public string JobDescription { get; set; } = "";
    public string CvText { get; set; } = "";
    public string CvContentHash { get; set; } = "";

    /// <summary>Set when this analysis was triggered from an inbox job; links the persisted report to it.</summary>
    public Guid? InboxJobId { get; set; }
}
