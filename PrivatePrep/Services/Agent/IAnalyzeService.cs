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
    int? OutputTokens = null);

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

public record AnalyzeRequestDto(string JobDescription);
