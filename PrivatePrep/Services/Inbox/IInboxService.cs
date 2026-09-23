using PrivatePrep.Models;

namespace PrivatePrep.Services.Inbox;

/// <summary>
/// Job postings a user collected into their inbox (browser extension or manual entry), pending
/// review and analysis. All methods scope strictly to the calling user's own rows.
/// </summary>
public interface IInboxService
{
    /// <summary>
    /// Creates a new entry, or updates title/company/raw text of an existing one for the same
    /// (userId, sourceUrl) pair — the extension re-POSTs the same posting as the user re-scans a page.
    /// </summary>
    Task<InboxJob> CreateOrUpdateAsync(string userId, CreateInboxJobRequest request, CancellationToken ct);

    /// <summary>Newest first (ExtractedAt DESC). limit is clamped to [1, 100].</summary>
    Task<IReadOnlyList<InboxJob>> ListForUserAsync(string userId, InboxJobStatus? status, int limit, CancellationToken ct);

    /// <summary>Null if the job does not exist or does not belong to this user (one outcome, not two errors).</summary>
    Task<InboxJob?> GetByIdForUserAsync(string userId, Guid id, CancellationToken ct);

    /// <summary>
    /// Updates only title/company/raw text (whichever are non-null in the request). Throws
    /// <see cref="KeyNotFoundException"/> if the job does not exist or does not belong to this user.
    /// </summary>
    Task<InboxJob> UpdateAsync(string userId, Guid id, UpdateInboxJobRequest request, CancellationToken ct);

    /// <summary>Hard delete. Returns false if the job did not exist or did not belong to this user.</summary>
    Task<bool> DeleteAsync(string userId, Guid id, CancellationToken ct);

    /// <summary>Count of this user's jobs with Status == New, for the "inbox is full" rate limit.</summary>
    Task<int> CountActiveForUserAsync(string userId, CancellationToken ct);

    /// <summary>Count of this user's jobs created within the given trailing window, for burst rate limiting.</summary>
    Task<int> CountRecentForUserAsync(string userId, TimeSpan window, CancellationToken ct);
}
