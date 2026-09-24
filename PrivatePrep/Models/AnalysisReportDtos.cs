namespace PrivatePrep.Models;

public sealed record AnalysisReportResponse(
    Guid Id,
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

/// <summary>Compact list shape: no full ReportJson, just what a report history list needs.</summary>
public sealed record AnalysisReportListItemResponse(
    Guid Id,
    Guid? InboxJobId,
    decimal? MatchScore,
    DateTime CreatedAt,
    string? InboxJobTitle,
    string? InboxJobCompany);
