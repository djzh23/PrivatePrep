using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivatePrep.Services.Tariffs;

/// <param name="Groups">Entgeltgruppe (e.g. "E9a") to the six Erfahrungsstufen, monthly gross EUR. A null step
/// means that group has no such stage (e.g. E1 and E15Ü have only five).</param>
public sealed record TariffTable(
    TariffAgreement Agreement,
    string Name,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    string Source,
    IReadOnlyDictionary<string, IReadOnlyList<decimal?>> Groups);

/// <summary>
/// Loads the versioned tariff tables shipped with the app (spec 001: "TVöD- und TV-L-Tabellen als versionierte
/// Daten im Repository"). Only the general/"Allgemeiner Teil" scales are seeded so far (source URLs and validity
/// dates are in the JSON files under Data/); sub-tables such as TVöD-SuE or TVöD-P are not, so a lookup for a
/// group only found in those returns no result rather than a guessed one — see the deferred decision "Pflege
/// der Tariftabellen" (the developer extends this at each Tarifrunde).
/// </summary>
public sealed class TariffTableCatalog
{
    private static readonly Lazy<TariffTableCatalog> Embedded = new(LoadEmbeddedCore);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<TariffAgreement, TariffTable> _byAgreement;

    public TariffTableCatalog(IReadOnlyList<TariffTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Tables = tables;
        _byAgreement = tables.ToDictionary(t => t.Agreement);
    }

    public IReadOnlyList<TariffTable> Tables { get; }

    public static TariffTableCatalog LoadEmbedded() => Embedded.Value;

    public bool TryGet(TariffAgreement agreement, out TariffTable table) =>
        _byAgreement.TryGetValue(agreement, out table!);

    private static TariffTableCatalog LoadEmbeddedCore() =>
        new([LoadOne("tvoed-vka.json"), LoadOne("tv-l.json")]);

    private static TariffTable LoadOne(string fileName)
    {
        var assembly = typeof(TariffTableCatalog).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded tariff table '{fileName}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded tariff table '{fileName}' could not be opened.");

        var dto = JsonSerializer.Deserialize<TariffTableDto>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Embedded tariff table '{fileName}' is empty or invalid.");

        return new TariffTable(
            Enum.Parse<TariffAgreement>(dto.Agreement),
            dto.Table,
            DateOnly.Parse(dto.ValidFrom),
            DateOnly.Parse(dto.ValidUntil),
            dto.Source,
            dto.Groups.ToDictionary(g => g.Key, g => (IReadOnlyList<decimal?>)g.Value, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class TariffTableDto
    {
        [JsonPropertyName("agreement")]
        public required string Agreement { get; set; }

        [JsonPropertyName("table")]
        public required string Table { get; set; }

        [JsonPropertyName("validFrom")]
        public required string ValidFrom { get; set; }

        [JsonPropertyName("validUntil")]
        public required string ValidUntil { get; set; }

        [JsonPropertyName("source")]
        public required string Source { get; set; }

        [JsonPropertyName("groups")]
        public required Dictionary<string, List<decimal?>> Groups { get; set; }
    }
}
