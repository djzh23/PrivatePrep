using Microsoft.EntityFrameworkCore;
using PrivatePrep.Data;
using PrivatePrep.Data.Entities;
using PrivatePrep.Models;

namespace PrivatePrep.Services.Inbox;

public sealed class InboxService(PrivatePrepDbContext db) : IInboxService
{
    public async Task<InboxJob> CreateOrUpdateAsync(string userId, CreateInboxJobRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        var existing = await db.InboxJobs
            .Where(x => x.UserId == userId && x.SourceUrl == request.SourceUrl)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            // Only these fields refresh on a re-collect; Status/ExtractedAt/SourceKind/Location stay
            // as first seen (matches the PATCH endpoint's "only title/company/rawText" contract).
            existing.Title = request.Title;
            existing.Company = request.Company;
            existing.RawText = request.RawText;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return ToDomain(existing);
        }

        var entity = new InboxJobEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = request.Title,
            Company = request.Company,
            Location = request.Location,
            SourceUrl = request.SourceUrl,
            SourceKind = request.SourceKind,
            RawText = request.RawText,
            Status = InboxJobStatus.New,
            ExtractedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.InboxJobs.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<InboxJob>> ListForUserAsync(string userId, InboxJobStatus? status, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var effectiveLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);

        var query = db.InboxJobs.AsNoTracking().Where(x => x.UserId == userId);
        if (status is not null)
            query = query.Where(x => x.Status == status);

        var rows = await query
            .OrderByDescending(x => x.ExtractedAt)
            .Take(effectiveLimit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<InboxJob?> GetByIdForUserAsync(string userId, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entity = await db.InboxJobs
            .AsNoTracking()
            .Where(x => x.Id == id && x.UserId == userId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<InboxJob> UpdateAsync(string userId, Guid id, UpdateInboxJobRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        var entity = await db.InboxJobs
            .Where(x => x.Id == id && x.UserId == userId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (entity is null)
            throw new KeyNotFoundException($"Inbox job {id} was not found for this user.");

        if (request.Title is not null) entity.Title = request.Title;
        if (request.Company is not null) entity.Company = request.Company;
        if (request.RawText is not null) entity.RawText = request.RawText;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDomain(entity);
    }

    public async Task<bool> DeleteAsync(string userId, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entity = await db.InboxJobs
            .Where(x => x.Id == id && x.UserId == userId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (entity is null)
            return false;

        db.InboxJobs.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public Task<int> CountActiveForUserAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return db.InboxJobs
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == InboxJobStatus.New)
            .CountAsync(ct);
    }

    public Task<int> CountRecentForUserAsync(string userId, TimeSpan window, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var since = DateTime.UtcNow - window;
        return db.InboxJobs
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.CreatedAt >= since)
            .CountAsync(ct);
    }

    private static InboxJob ToDomain(InboxJobEntity e) => new(
        e.Id,
        e.UserId,
        e.Title,
        e.Company,
        e.Location,
        e.SourceUrl,
        e.SourceKind,
        e.RawText,
        e.Status,
        e.ExtractedAt,
        e.AnalyzedAt,
        e.AnalysisReportId,
        e.CreatedAt,
        e.UpdatedAt);
}
