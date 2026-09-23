namespace PrivatePrep.Models;

public sealed record CreateInboxJobRequest(
    string Title,
    string Company,
    string? Location,
    string SourceUrl,
    InboxSourceKind SourceKind,
    string RawText);

/// <summary>Only these three fields can be changed after creation; everything else (Status, ExtractedAt, ...) stays.</summary>
public sealed record UpdateInboxJobRequest(
    string? Title,
    string? Company,
    string? RawText);

public sealed record InboxJobResponse(
    Guid Id,
    string Title,
    string Company,
    string? Location,
    string SourceUrl,
    InboxSourceKind SourceKind,
    string RawText,
    InboxJobStatus Status,
    DateTime ExtractedAt,
    DateTime? AnalyzedAt,
    Guid? AnalysisReportId);

/// <summary>Compact list shape: no full RawText, just a short preview.</summary>
public sealed record InboxJobListItemResponse(
    Guid Id,
    string Title,
    string Company,
    string? Location,
    string SourceUrl,
    InboxSourceKind SourceKind,
    InboxJobStatus Status,
    DateTime ExtractedAt,
    DateTime? AnalyzedAt,
    Guid? AnalysisReportId,
    string RawTextPreview);
