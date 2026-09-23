namespace PrivatePrep.Models;

/// <summary>
/// Domain model returned by <see cref="Services.Inbox.IInboxService"/>, mapped to/from
/// <see cref="Data.Entities.InboxJobEntity"/> inside the service. Controllers map this to
/// <see cref="InboxJobResponse"/>/<see cref="InboxJobListItemResponse"/> for the API.
/// </summary>
public sealed record InboxJob(
    Guid Id,
    string UserId,
    string Title,
    string Company,
    string? Location,
    string SourceUrl,
    InboxSourceKind SourceKind,
    string RawText,
    InboxJobStatus Status,
    DateTime ExtractedAt,
    DateTime? AnalyzedAt,
    Guid? AnalysisReportId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
