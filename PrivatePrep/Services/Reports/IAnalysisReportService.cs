using PrivatePrep.Models;

namespace PrivatePrep.Services.Reports;

/// <summary>
/// Persisted results of the analyze pipeline. All methods scope strictly to the calling user's
/// own rows.
/// </summary>
public interface IAnalysisReportService
{
    /// <summary>
    /// When <paramref name="inboxJobId"/> is given: updates the existing report for that
    /// (userId, inboxJobId) pair if one exists, otherwise creates one — then sets the inbox job's
    /// AnalysisReportId/Status(Analyzed)/AnalyzedAt in the same transaction. Throws
    /// <see cref="KeyNotFoundException"/> if that inbox job does not exist or does not belong to
    /// this user.
    ///
    /// When <paramref name="inboxJobId"/> is null: always creates a new report (no upsert key
    /// exists for ad-hoc analyses).
    /// </summary>
    Task<AnalysisReport> UpsertAsync(
        string userId,
        Guid? inboxJobId,
        string reportJson,
        string cvHash,
        string jdHash,
        int cvLength,
        int jdLength,
        string llmModel,
        decimal? matchScore,
        CancellationToken ct);

    /// <summary>Null if the report does not exist or does not belong to this user (one outcome, not two errors).</summary>
    Task<AnalysisReport?> GetByIdForUserAsync(string userId, Guid id, CancellationToken ct);

    /// <summary>The report belonging to a given inbox job, if any. Null if no report exists yet, or the job is not this user's.</summary>
    Task<AnalysisReport?> GetByInboxJobIdForUserAsync(string userId, Guid inboxJobId, CancellationToken ct);

    /// <summary>Newest first (CreatedAt DESC). limit is clamped to [1, 100].</summary>
    Task<IReadOnlyList<AnalysisReport>> ListForUserAsync(string userId, int limit, CancellationToken ct);

    /// <summary>
    /// Hard delete. Returns false if the report did not exist or did not belong to this user.
    /// If the report belonged to an inbox job, that job is reset to Status = New with
    /// AnalyzedAt/AnalysisReportId cleared, so it becomes analyzable again.
    /// </summary>
    Task<bool> DeleteAsync(string userId, Guid id, CancellationToken ct);
}
