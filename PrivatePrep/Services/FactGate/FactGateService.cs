using System.Text.RegularExpressions;
using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Services.FactGate;

public sealed partial class FactGateService : IFactGateService
{
    private static readonly string[] BannedPhrases =
    [
        "nachweisliche erfahrung",
        "proven track record",
        "passionate about",
        "results-oriented",
        "cutting-edge",
        "ergebnisorientiert",
        "leidenschaftlich",
        "hochmoderne",
        "holistic",
        "seamless",
        "innovative",
        "innovativ",
        "modernste",
        "tapestry",
        "leveraging",
        "leveraged",
        "leverage",
        "delving",
        "delved",
        "robust",
        "delve",
    ];

    private readonly SkillTaxonomyCatalog _taxonomy;

    public FactGateService()
        : this(SkillTaxonomyCatalog.LoadEmbedded())
    {
    }

    public FactGateService(SkillTaxonomyCatalog taxonomy)
    {
        ArgumentNullException.ThrowIfNull(taxonomy);
        _taxonomy = taxonomy;
    }

    public FactGateResult Verify(string generatedOutput, FactGateContext context)
    {
        generatedOutput ??= string.Empty;
        context ??= new FactGateContext("", "", []);

        var sources = $"{context.CvText}\n{context.StoryText}";
        var violations = new List<FactGateViolation>();

        CollectInventedSkills(generatedOutput, context, sources, violations);
        CollectInventedMetrics(generatedOutput, sources, violations);
        CollectBannedWords(generatedOutput, violations);

        return new FactGateResult(violations.Count == 0, violations);
    }

    private void CollectInventedSkills(
        string output,
        FactGateContext context,
        string sources,
        List<FactGateViolation> violations)
    {
        foreach (var skill in _taxonomy.Skills)
        {
            if (!_taxonomy.MatchesDirect(output, skill))
                continue;

            if (IsSkillAllowed(skill, context, sources))
                continue;

            violations.Add(new FactGateViolation(
                FactGateViolationTypes.InventedSkill,
                skill.Name,
                $"Skill '{skill.Name}' appears in the generated output but not in the CV, story, or allowed-skill list."));
        }
    }

    private bool IsSkillAllowed(SkillTaxonomyEntry skill, FactGateContext context, string sources)
    {
        foreach (var allowed in context.AllowedSkills)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;

            if (string.Equals(allowed, skill.Name, StringComparison.OrdinalIgnoreCase))
                return true;

            if (_taxonomy.TryGet(allowed, out var allowedSkill)
                && string.Equals(allowedSkill.Name, skill.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return _taxonomy.MentionedInCv(sources, skill);
    }

    private static void CollectInventedMetrics(string output, string sources, List<FactGateViolation> violations)
    {
        foreach (Match match in MetricRegex().Matches(output))
        {
            var snippet = match.Value.Trim();
            var number = match.Groups[1].Value;
            if (IsMetricGrounded(snippet, number, sources))
                continue;

            violations.Add(new FactGateViolation(
                FactGateViolationTypes.InventedMetric,
                snippet,
                $"Metric '{snippet}' is not grounded in the CV or story."));
        }
    }

    private static bool IsMetricGrounded(string snippet, string number, string sources)
    {
        if (sources.Contains(snippet, StringComparison.OrdinalIgnoreCase))
            return true;

        var normalized = NormalizeNumber(number);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (NumberAppears(sources, normalized))
            return true;

        var swapped = normalized.Contains('.')
            ? normalized.Replace('.', ',')
            : normalized.Replace(',', '.');

        return swapped != normalized && NumberAppears(sources, swapped);
    }

    private static bool NumberAppears(string sources, string number)
    {
        var pattern = $@"(?<![0-9]){Regex.Escape(number)}(?![0-9])";
        return Regex.IsMatch(sources, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    }

    private static string NormalizeNumber(string raw)
    {
        var value = raw.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (Regex.IsMatch(value, @"^\d{1,3}(,\d{3})+$"))
            return value.Replace(",", "", StringComparison.Ordinal);
        if (Regex.IsMatch(value, @"^\d{1,3}(\.\d{3})+$"))
            return value.Replace(".", "", StringComparison.Ordinal);
        return value;
    }

    private static void CollectBannedWords(string output, List<FactGateViolation> violations)
    {
        foreach (var phrase in BannedPhrases)
        {
            var regex = BannedPhraseRegex(phrase);
            foreach (Match match in regex.Matches(output))
            {
                violations.Add(new FactGateViolation(
                    FactGateViolationTypes.BannedWord,
                    SnippetAround(output, match.Index, match.Length),
                    $"Banned phrase '{phrase}' is not allowed in generated output."));
            }
        }
    }

    private static Regex BannedPhraseRegex(string phrase)
    {
        var escaped = Regex.Escape(phrase);
        return new Regex(
            $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(150));
    }

    private static string SnippetAround(string text, int index, int length)
    {
        var start = Math.Max(0, index - 20);
        var end = Math.Min(text.Length, index + length + 20);
        return text[start..end].Trim();
    }

    [GeneratedRegex(
        """\b(\d+(?:[.,]\d+)?)\s*(%|(?:[kKmMbB]|[xX]|Millionen|Million|Milliarden|Milliarde|Nutzer|Zeilen|Jahre|Jahr|Monate|Monat|Tage|Tag|Stunden|Stunde|Mitarbeiter|Kunden|Prozent|Teams|Projekte|Tickets|Users?|Customers?|Years?|Year|Lines?|Deployments?|Requests?)\b)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetricRegex();
}
