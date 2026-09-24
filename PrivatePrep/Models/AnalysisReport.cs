namespace PrivatePrep.Models;

/// <summary>
/// Domain model returned by <see cref="Services.Reports.IAnalysisReportService"/>, mapped to/from
/// <see cref="Data.Entities.AnalysisReportEntity"/> inside the service. Controllers map this to
/// <see cref="AnalysisReportResponse"/>/<see cref="AnalysisReportListItemResponse"/> for the API.
/// </summary>
public sealed record AnalysisReport(
    Guid Id,
    string UserId,
    Guid? InboxJobId,
    string ReportJson,
    string CvHash,
    string JdHash,
    int CvLength,
    int JdLength,
    string LlmModel,
    decimal? MatchScore,
    DateTime CreatedAt,
    DateTime UpdatedAt);
