using Microsoft.EntityFrameworkCore;
using PrivatePrep.Data;
using PrivatePrep.Data.Entities;
using PrivatePrep.Models;

namespace PrivatePrep.Services.Reports;

public sealed class AnalysisReportService(PrivatePrepDbContext db) : IAnalysisReportService
{
    public async Task<AnalysisReport> UpsertAsync(
        string userId,
        Guid? inboxJobId,
        string reportJson,
        string cvHash,
        string jdHash,
        int cvLength,
        int jdLength,
        string llmModel,
        decimal? matchScore,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportJson);

        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            AnalysisReportEntity entity;

            if (inboxJobId is Guid jobId)
            {
                // Validate ownership up front: without this, a caller could pass another user's
                // inboxJobId and have a report row FK-linked to a job it does not own (the FK
                // itself is not user-scoped, only the composite unique index is).
                var job = await db.InboxJobs
                    .Where(x => x.Id == jobId && x.UserId == userId)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (job is null)
                    throw new KeyNotFoundException($"Inbox job {jobId} was not found for this user.");

                var existing = await db.AnalysisReports
                    .Where(x => x.UserId == userId && x.InboxJobId == jobId)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    existing.ReportJson = reportJson;
                    existing.CvHash = cvHash;
                    existing.JdHash = jdHash;
                    existing.CvLength = cvLength;
                    existing.JdLength = jdLength;
                    existing.LlmModel = llmModel;
                    existing.MatchScore = matchScore;
                    existing.UpdatedAt = now;
                    entity = existing;
                }
                else
                {
                    entity = new AnalysisReportEntity
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        InboxJobId = jobId,
                        ReportJson = reportJson,
                        CvHash = cvHash,
                        JdHash = jdHash,
                        CvLength = cvLength,
                        JdLength = jdLength,
                        LlmModel = llmModel,
                        MatchScore = matchScore,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    db.AnalysisReports.Add(entity);
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                job.AnalysisReportId = entity.Id;
                job.Status = InboxJobStatus.Analyzed;
                job.AnalyzedAt = now;
                job.UpdatedAt = now;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            else
            {
                // No upsert key without an inbox job: every ad-hoc analysis gets its own row.
                entity = new AnalysisReportEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InboxJobId = null,
                    ReportJson = reportJson,
                    CvHash = cvHash,
                    JdHash = jdHash,
                    CvLength = cvLength,
                    JdLength = jdLength,
                    LlmModel = llmModel,
                    MatchScore = matchScore,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.AnalysisReports.Add(entity);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return ToDomain(entity);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AnalysisReport?> GetByIdForUserAsync(string userId, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entity = await db.AnalysisReports
            .AsNoTracking()
            .Where(x => x.Id == id && x.UserId == userId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<AnalysisReport?> GetByInboxJobIdForUserAsync(string userId, Guid inboxJobId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entity = await db.AnalysisReports
            .AsNoTracking()
            .Where(x => x.InboxJobId == inboxJobId && x.UserId == userId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<AnalysisReport>> ListForUserAsync(string userId, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var effectiveLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);

        var rows = await db.AnalysisReports
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(effectiveLimit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<bool> DeleteAsync(string userId, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var report = await db.AnalysisReports
                .Where(x => x.Id == id && x.UserId == userId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (report is null)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return false;
            }

            var inboxJobId = report.InboxJobId;
            db.AnalysisReports.Remove(report);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (inboxJobId is Guid jobId)
            {
                var job = await db.InboxJobs
                    .Where(x => x.Id == jobId && x.UserId == userId)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                // Guard against a job whose AnalysisReportId already moved on to a different
                // report (should not happen given the one-report-per-job unique index, but cheap
                // to check before clobbering a link that is no longer this report's).
                if (job is not null && job.AnalysisReportId == id)
                {
                    job.Status = InboxJobStatus.New;
                    job.AnalyzedAt = null;
                    job.AnalysisReportId = null;
                    job.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private static AnalysisReport ToDomain(AnalysisReportEntity e) => new(
        e.Id,
        e.UserId,
        e.InboxJobId,
        e.ReportJson,
        e.CvHash,
        e.JdHash,
        e.CvLength,
        e.JdLength,
        e.LlmModel,
        e.MatchScore,
        e.CreatedAt,
        e.UpdatedAt);
}
