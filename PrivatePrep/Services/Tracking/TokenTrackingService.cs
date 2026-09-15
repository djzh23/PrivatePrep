using PrivatePrep.Models;

namespace PrivatePrep.Services.Tracking;

public readonly record struct TokenTrackingBackendInfo(
    string EffectiveStorage,
    string ConfiguredTokenUsageStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>Token usage metrics (PostgreSQL only in V1).</summary>
public class TokenTrackingService
{
    private readonly TokenTrackingPostgresService? _postgres;

    /// <summary>Moq-friendly constructor.</summary>
    protected TokenTrackingService()
    {
    }

    public TokenTrackingService(TokenTrackingPostgresService postgres)
    {
        _postgres = postgres;
    }

    private TokenTrackingPostgresService Postgres =>
        _postgres ?? throw new InvalidOperationException("TokenTrackingService was constructed without PostgreSQL.");

    public virtual TokenTrackingBackendInfo GetBackendInfo() =>
        new("postgres", "postgres", Degraded: false, DegradedReason: null);

    public static decimal CalculateCost(
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0) =>
        TokenTrackingCostHelper.CalculateCost(model, inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens);

    public static string? ParseTopToolFromTcFields(Dictionary<string, string> map) =>
        TokenTrackingCostHelper.ParseTopToolFromTcFields(map);

    public virtual Task TrackUsageAsync(
        string userId,
        string toolType,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0) =>
        Postgres.TrackUsageAsync(
            userId,
            toolType,
            model,
            inputTokens,
            outputTokens,
            cacheCreationInputTokens,
            cacheReadInputTokens,
            CancellationToken.None);

    public virtual Task<AdminDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default) =>
        Postgres.GetDashboardDataAsync(cancellationToken);

    public virtual Task<UserUsageSummary> GetUserUsageAsync(
        string userId,
        string startDate,
        string endDate,
        CancellationToken cancellationToken = default) =>
        Postgres.GetUserUsageAsync(userId, startDate, endDate, cancellationToken);

    public virtual Task<List<UserUsageSummary>> GetTopUsersAsync(string date, int limit = 20, CancellationToken cancellationToken = default) =>
        Postgres.GetTopUsersAsync(date, limit, cancellationToken);

    public virtual Task<List<UserUsageSummary>> GetTopUsersDateRangeAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default) =>
        Postgres.GetTopUsersDateRangeAsync(startUtc, endUtc, limit, cancellationToken);

    public virtual Task<List<UserUsageSummary>> GetTopUsersForDateRangeQueryAsync(
        string startDate,
        string endDate,
        int limit,
        CancellationToken cancellationToken = default) =>
        Postgres.GetTopUsersForDateRangeQueryAsync(startDate, endDate, limit, cancellationToken);

    public virtual Task<List<DailyUsage>> GetDailyStatsAsync(int days, CancellationToken cancellationToken = default) =>
        Postgres.GetDailyStatsAsync(days, cancellationToken);
}
