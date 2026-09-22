namespace PrivatePrep.Services.Tariffs;

/// <param name="IsStale">True when the table's "Stand" (<paramref name="ValidFrom"/>) is more than 12 months
/// old (AC-8): a newer Tarifrunde may already exist and the report should say so.</param>
public sealed record TariffLookupResult(
    TariffAgreement Agreement,
    string Group,
    IReadOnlyList<decimal?> MonthlyGrossPerStep,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    string Source,
    bool IsStale);

public interface ITariffTableService
{
    /// <summary>
    /// Looks up the monthly gross salary steps for a group. Null when the agreement is not seeded at all, or
    /// the group belongs to a sub-table (e.g. TVöD-SuE, TVöD-P) that is not — never a guessed figure (AC-2).
    /// </summary>
    TariffLookupResult? Lookup(TariffAgreement agreement, string group, DateOnly today);
}

public sealed class TariffTableService(TariffTableCatalog catalog) : ITariffTableService
{
    public TariffTableService() : this(TariffTableCatalog.LoadEmbedded())
    {
    }

    public TariffLookupResult? Lookup(TariffAgreement agreement, string group, DateOnly today)
    {
        if (!catalog.TryGet(agreement, out var table))
            return null;

        var key = NormalizeForLookup(group);
        if (key.Length == 0 || !table.Groups.TryGetValue(key, out var steps))
            return null;

        return new TariffLookupResult(
            agreement,
            key,
            steps,
            table.ValidFrom,
            table.ValidUntil,
            table.Source,
            today > table.ValidFrom.AddMonths(12));
    }

    /// <summary>A bare group like "6" or "9a" means the general scale ("E6", "E9a"). A letter-prefixed group
    /// (e.g. "S8a", "P8") names a sub-table scale and is left as-is, so it correctly misses here if not seeded.</summary>
    private static string NormalizeForLookup(string raw)
    {
        var compact = (raw ?? "").Replace(" ", "").Trim();
        if (compact.Length == 0)
            return compact;

        return char.IsDigit(compact[0])
            ? "E" + compact
            : char.ToUpperInvariant(compact[0]) + compact[1..];
    }
}
