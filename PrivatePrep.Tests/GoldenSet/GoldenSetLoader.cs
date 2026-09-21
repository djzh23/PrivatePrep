using System.Text.Json;

namespace PrivatePrep.Tests.GoldenSet;

public static class GoldenSetLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The cases the rules are developed against.</summary>
    public static IReadOnlyList<GoldenCase> LoadAll() => Load("Cases");

    /// <summary>
    /// Postings written after the rules, with different phrasing, to measure how well they generalise.
    /// Labelled for must-haves and salary only; tariff and signals are not evaluated here.
    /// </summary>
    public static IReadOnlyList<GoldenCase> LoadHoldout() => Load("Holdout");

    private static IReadOnlyList<GoldenCase> Load(string folder)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "GoldenSet", folder);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Golden set directory not found: {directory}");

        return Directory.GetFiles(directory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(file => JsonSerializer.Deserialize<GoldenCase>(File.ReadAllText(file), Options)
                ?? throw new InvalidDataException($"Golden case is empty: {file}"))
            .ToList();
    }
}
