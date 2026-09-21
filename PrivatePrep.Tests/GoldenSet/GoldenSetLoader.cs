using System.Text.Json;

namespace PrivatePrep.Tests.GoldenSet;

public static class GoldenSetLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<GoldenCase> LoadAll()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "GoldenSet", "Cases");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Golden set directory not found: {directory}");

        return Directory.GetFiles(directory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(file => JsonSerializer.Deserialize<GoldenCase>(File.ReadAllText(file), Options)
                ?? throw new InvalidDataException($"Golden case is empty: {file}"))
            .ToList();
    }
}
