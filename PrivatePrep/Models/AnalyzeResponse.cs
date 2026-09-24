using PrivatePrep.Services.Agent;

namespace PrivatePrep.Models;

/// <summary>
/// Wraps a persisted <see cref="AnalyzeReport"/> with its new database id. Inherits from
/// AnalyzeReport (deliberately not sealed) instead of redeclaring its fields, so the JSON response
/// stays exactly backward compatible: every existing field serializes at the same top level as
/// before, with reportId simply added alongside. Existing consumers that do not know about
/// reportId are unaffected.
/// </summary>
public sealed record AnalyzeResponse : AnalyzeReport
{
    public Guid ReportId { get; init; }

    public AnalyzeResponse(AnalyzeReport report, Guid reportId)
        : base(
            report.GlobalScore,
            report.Dimensions,
            report.SkillGap,
            report.Bullets,
            report.RoleSummary,
            report.Warnings,
            report.CultureScreen,
            report.FactViolations,
            report.ModelUsed,
            report.InputTokens,
            report.OutputTokens,
            report.UnverifiedBullets)
    {
        ReportId = reportId;
    }
}
