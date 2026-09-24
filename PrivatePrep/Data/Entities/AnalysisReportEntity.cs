using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrivatePrep.Data.Entities;

/// <summary>
/// One persisted result of the analyze pipeline (PrivatePrep.Services.Agent.AnalyzeReport),
/// optionally linked to the inbox job it was run from. This is the report-half exception to spec
/// 001's original "nothing is stored" principle (see docs/specs/001-analyse-v2.md, 2026-09-24
/// addendum) — the CV raw text itself is still never stored here, only its hash.
/// </summary>
[Table("analysis_reports")]
public sealed class AnalysisReportEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("clerk_user_id")]
    public string UserId { get; set; } = "";

    /// <summary>Set when this analysis was run from an inbox job. Null for ad-hoc analyses.</summary>
    [Column("inbox_job_id")]
    public Guid? InboxJobId { get; set; }

    public InboxJobEntity? InboxJob { get; set; }

    /// <summary>Full serialized AnalyzeReport JSON, camelCase, exactly as the API returns it.</summary>
    [Column("report_json")]
    [MaxLength(100_000)]
    public string ReportJson { get; set; } = "";

    /// <summary>SHA-256 hex of the CV text used for this analysis. The CV text itself is never stored.</summary>
    [Column("cv_hash")]
    [MaxLength(64)]
    public string CvHash { get; set; } = "";

    /// <summary>SHA-256 hex of the job description text used for this analysis.</summary>
    [Column("jd_hash")]
    [MaxLength(64)]
    public string JdHash { get; set; } = "";

    [Column("cv_length")]
    public int CvLength { get; set; }

    [Column("jd_length")]
    public int JdLength { get; set; }

    [Column("llm_model")]
    [MaxLength(200)]
    public string LlmModel { get; set; } = "";

    /// <summary>Extracted AnalyzeReport.GlobalScore (0.0-5.0), duplicated here for fast list rendering
    /// without deserializing ReportJson.</summary>
    [Column("match_score")]
    public decimal? MatchScore { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
