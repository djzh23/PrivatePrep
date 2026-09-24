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
    IReadOnlyList<BulletRewriteSuggestion>? UnverifiedBullets = null);

public record ScoreDimensions(
    decimal CvMatch,
    decimal RoleAlignment,
    decimal Culture,
    decimal RedFlags);

public record BulletRewriteSuggestion(
    string OriginalBullet,
    string RewrittenBullet,
    string Reasoning);

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
