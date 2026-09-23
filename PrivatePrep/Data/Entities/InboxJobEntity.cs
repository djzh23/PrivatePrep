using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PrivatePrep.Models;

namespace PrivatePrep.Data.Entities;

/// <summary>
/// One job posting collected into a user's inbox (browser extension "job vacuum" or manual entry),
/// held as raw text until the user deletes it or runs it through the analyze pipeline. This is the
/// one deliberate exception to "no full-text storage" (spec 001) — the analyze endpoints themselves
/// still never persist a posting or a report.
/// </summary>
[Table("inbox_jobs")]
public sealed class InboxJobEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("clerk_user_id")]
    public string UserId { get; set; } = "";

    [Column("title")]
    [MaxLength(500)]
    public string Title { get; set; } = "";

    [Column("company")]
    [MaxLength(300)]
    public string Company { get; set; } = "";

    [Column("location")]
    [MaxLength(300)]
    public string? Location { get; set; }

    [Column("source_url")]
    [MaxLength(2000)]
    public string SourceUrl { get; set; } = "";

    [Column("source_kind")]
    public InboxSourceKind SourceKind { get; set; }

    [Column("raw_text")]
    [MaxLength(50_000)]
    public string RawText { get; set; } = "";

    [Column("status")]
    public InboxJobStatus Status { get; set; } = InboxJobStatus.New;

    /// <summary>When the posting was collected. Set once at creation, never touched by the upsert's update path.</summary>
    [Column("extracted_at")]
    public DateTime ExtractedAt { get; set; }

    [Column("analyzed_at")]
    public DateTime? AnalyzedAt { get; set; }

    /// <summary>
    /// Optional correlation id for a future persisted analysis report. No such table exists yet
    /// (analyses are not stored server side, spec 001), so this carries no foreign key.
    /// </summary>
    [Column("analysis_report_id")]
    public Guid? AnalysisReportId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
