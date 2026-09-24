using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivatePrep.Services.SkillGap;

public sealed class SkillTaxonomyEntry
{
    public required string Name { get; init; }
    public IReadOnlyList<string> MatchTokens { get; init; } = [];
    public IReadOnlyList<string> Implies { get; init; } = [];
    public IReadOnlyList<string> CoveredBy { get; init; } = [];
}

public sealed class SkillTaxonomyCatalog
{
    private static readonly Lazy<SkillTaxonomyCatalog> Embedded = new(LoadEmbeddedCore);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, SkillTaxonomyEntry> _byName;

    public SkillTaxonomyCatalog(IReadOnlyList<SkillTaxonomyEntry> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        Skills = skills;
        _byName = new Dictionary<string, SkillTaxonomyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
            _byName[skill.Name] = skill;
    }

    public IReadOnlyList<SkillTaxonomyEntry> Skills { get; }

    public static SkillTaxonomyCatalog LoadEmbedded() => Embedded.Value;

    public bool TryGet(string name, out SkillTaxonomyEntry entry) =>
        _byName.TryGetValue(name, out entry!);

    public IReadOnlyList<string> DirectTokensFor(SkillTaxonomyEntry skill)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { skill.Name };
        foreach (var token in skill.MatchTokens)
        {
            if (!string.IsNullOrWhiteSpace(token))
                tokens.Add(token.Trim());
        }

        return [.. tokens.OrderByDescending(t => t.Length)];
    }

    public bool MatchesDirect(string text, SkillTaxonomyEntry skill) =>
        SkillMention.AnyAppearsIn(text, DirectTokensFor(skill));

    public bool MentionedInCv(string text, SkillTaxonomyEntry skill)
    {
        if (MatchesDirect(text, skill))
            return true;

        foreach (var parent in skill.CoveredBy)
        {
            if (_byName.TryGetValue(parent, out var parentSkill) && MatchesDirect(text, parentSkill))
                return true;
        }

        return false;
    }

    private static SkillTaxonomyCatalog LoadEmbeddedCore()
    {
        var fieldSkills = LoadFieldFiles();
        var en = Parse(ReadEmbedded("skill-taxonomy.en.json"));
        return Merge(fieldSkills, en);
    }

    private static List<SkillTaxonomyEntry> LoadFieldFiles()
    {
        var assembly = typeof(SkillTaxonomyCatalog).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(IsFieldTaxonomyResource)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resources.Length == 0)
            throw new InvalidOperationException("No field skill-taxonomy JSON files were embedded.");

        var result = new List<SkillTaxonomyEntry>();
        foreach (var resource in resources)
            result.AddRange(Parse(ReadResource(resource)));

        return result;
    }

    private static bool IsFieldTaxonomyResource(string resourceName) =>
        resourceName.Contains("taxonomy-draft", StringComparison.OrdinalIgnoreCase)
        && resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static SkillTaxonomyCatalog Merge(IReadOnlyList<SkillTaxonomyEntry> left, IReadOnlyList<SkillTaxonomyEntry> right)
    {
        var map = new Dictionary<string, SkillTaxonomyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in left.Concat(right))
        {
            if (map.TryGetValue(skill.Name, out var existing))
            {
                map[skill.Name] = new SkillTaxonomyEntry
                {
                    Name = existing.Name,
                    MatchTokens = Union(existing.MatchTokens, skill.MatchTokens),
                    Implies = Union(existing.Implies, skill.Implies),
                    CoveredBy = Union(existing.CoveredBy, skill.CoveredBy),
                };
            }
            else
            {
                map[skill.Name] = skill;
            }
        }

        return new SkillTaxonomyCatalog([.. map.Values.OrderByDescending(s => s.Name.Length)]);
    }

    private static IReadOnlyList<string> Union(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in a.Concat(b))
        {
            if (!string.IsNullOrWhiteSpace(item))
                set.Add(item.Trim());
        }

        return [.. set];
    }

    private static List<SkillTaxonomyEntry> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<TaxonomyFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Skill taxonomy JSON is empty.");

        var result = new List<SkillTaxonomyEntry>();
        foreach (var dto in file.Skills)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                continue;

            result.Add(new SkillTaxonomyEntry
            {
                Name = dto.Name.Trim(),
                MatchTokens = dto.Aliases ?? [],
                Implies = dto.Implies ?? [],
                CoveredBy = dto.CoveredBy ?? [],
            });
        }

        return result;
    }

    private static string ReadEmbedded(string fileName)
    {
        var assembly = typeof(SkillTaxonomyCatalog).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded skill taxonomy '{fileName}' was not found.");

        return ReadResource(resource);
    }

    private static string ReadResource(string resource)
    {
        var assembly = typeof(SkillTaxonomyCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded skill taxonomy '{resource}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class TaxonomyFile
    {
        [JsonPropertyName("skills")]
        public List<TaxonomySkillDto> Skills { get; set; } = [];
    }

    private sealed class TaxonomySkillDto
    {
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public List<string>? Implies { get; set; }
        public List<string>? CoveredBy { get; set; }
    }
}
