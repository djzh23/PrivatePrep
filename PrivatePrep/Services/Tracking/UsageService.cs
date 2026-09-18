using Microsoft.Extensions.Caching.Memory;
using PrivatePrep.Data;

namespace PrivatePrep.Services.Tracking;

public readonly record struct UsageBackendInfo(
    string EffectiveStorage,
    string ConfiguredUsageStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>Daily usage limits and Stripe customer mapping (PostgreSQL; webhook idempotency in memory).</summary>
public class UsageService
{
    private readonly UsagePostgresService? _postgres;
    private readonly IMemoryCache? _cache;

    /// <summary>Moq-friendly constructor.</summary>
    protected UsageService()
    {
    }

    public UsageService(UsagePostgresService postgres, IMemoryCache cache)
    {
        _postgres = postgres;
        _cache = cache;
    }

    private UsagePostgresService Postgres =>
        _postgres ?? throw new InvalidOperationException("UsageService was constructed without PostgreSQL.");

    public virtual UsageBackendInfo GetBackendInfo() =>
        new("postgres", "postgres", Degraded: false, DegradedReason: null);

    public virtual Task<int> GetUsageTodayAsync(string userId) => Postgres.GetUsageTodayAsync(userId);

    public virtual Task<int> GetUsageTodayStrictAsync(string userId) => Postgres.GetUsageTodayStrictAsync(userId);

    public virtual Task<int> IncrementUsageAsync(string userId) => Postgres.IncrementUsageAsync(userId);

    public virtual Task<string> GetPlanAsync(string userId) => Postgres.GetPlanAsync(userId);

    public virtual Task<string> GetPlanStrictAsync(string userId) => Postgres.GetPlanStrictAsync(userId);

    public virtual Task<(string Plan, int UsageToday)> GetUsageSnapshotAsync(string userId) =>
        Postgres.GetUsageSnapshotAsync(userId);

    public virtual Task SetPlanAsync(string userId, string plan) => Postgres.SetPlanAsync(userId, plan);

    public virtual Task SetStripeCustomerIdAsync(string userId, string customerId) =>
        Postgres.SetStripeCustomerIdAsync(userId, customerId);

    public virtual Task<string?> GetUserIdByStripeCustomerIdAsync(string customerId) =>
        Postgres.GetUserIdByStripeCustomerIdAsync(customerId);

    public virtual Task<string?> GetStripeCustomerIdAsync(string userId) =>
        Postgres.GetStripeCustomerIdAsync(userId);

    public virtual Task<bool> TryAcquireStripeEventAsync(string eventId)
    {
        var cache = _cache ?? throw new InvalidOperationException("UsageService was constructed without memory cache.");
        var key = $"stripe:event:{eventId}";
        if (cache.TryGetValue(key, out _))
            return Task.FromResult(false);

        cache.Set(key, true, TimeSpan.FromHours(48));
        return Task.FromResult(true);
    }

    public virtual Task RecordStripeWebhookAuditAsync(StripeWebhookAuditRecord audit) =>
        Task.CompletedTask;

    public virtual async Task<StripeDebugInfo> GetStripeDebugInfoAsync(string userId)
    {
        var currentPlan = await GetPlanStrictAsync(userId).ConfigureAwait(false);
        return new StripeDebugInfo(userId, currentPlan, null, null, null);
    }

    public static int GetDailyLimit(string plan) => plan switch
    {
        "anonymous" => 1,
        "free" => 1,
        "premium" => 30,
        _ => 1,
    };

    public virtual Task<UsageCheckResult> CheckAndIncrementAsync(string userId, bool isAnonymous) =>
        Postgres.CheckAndIncrementAsync(userId, isAnonymous);
}

public sealed class UsageCheckResult
{
    public bool Allowed { get; set; }
    public string? Reason { get; set; }
    public string? Message { get; set; }
    public int UsageToday { get; set; }
    public int DailyLimit { get; set; }
    public string Plan { get; set; } = "free";
}

public sealed record StripeWebhookAuditRecord(
    string EventId,
    string EventType,
    string? SessionId,
    string? UserId,
    string? Plan,
    string ProcessedAt,
    string? CustomerId,
    string? SubscriptionId,
    string Result);

public sealed record StripeDebugInfo(
    string UserId,
    string CurrentPlan,
    string? LastStripeEventId,
    string? LastStripeEventAt,
    string? LastCheckoutSessionId);
