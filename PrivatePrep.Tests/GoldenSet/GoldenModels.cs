namespace PrivatePrep.Tests.GoldenSet;

/// <summary>One CV/posting pair with the result a correct analysis must reproduce.</summary>
public sealed record GoldenCase(
    string Id,
    string Field,
    string Origin,
    string Language,
    bool IsJobPosting,
    string JobDescription,
    string? CvText,
    GoldenExpectations Expected);

public sealed record GoldenExpectations(
    IReadOnlyList<ExpectedMustHave> MustHaves,
    string? SalaryVerbatim,
    ExpectedTariff? Tariff,
    IReadOnlyList<string> Signals);

/// <summary>
/// A hard requirement of the posting and the exact posting text it comes from. <paramref name="Status"/> says
/// how the CV fares and is only set for cases that carry a CV; CV-free edge cases check extraction alone.
/// </summary>
public sealed record ExpectedMustHave(string Kind, string Quote, string? Status = null);

public sealed record ExpectedTariff(string Agreement, string? Group);

public static class GoldenVocabulary
{
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>
    {
        "pflege", "verwaltung", "vertrieb", "handwerk", "bildung", "it",
    };

    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>
    {
        "abschluss", "berufserfahrung", "fuehrerschein", "schicht", "sprache", "zertifikat", "arbeitszeit", "einsatzort",
    };

    /// <summary>How the candidate's CV relates to the requirement.</summary>
    public static readonly IReadOnlySet<string> Statuses = new HashSet<string>
    {
        "met", "notMet", "unclear",
    };

    public static readonly IReadOnlySet<string> Signals = new HashSet<string>
    {
        "contradictory_requirements",
        "warning_language",
        "pseudo_freelance_or_commission_only",
        "missing_information",
        "ai_instruction",
    };

    public static readonly IReadOnlySet<string> Languages = new HashSet<string> { "de", "en" };

    public static readonly IReadOnlySet<string> Origins = new HashSet<string> { "synthetic", "real" };

    public static readonly IReadOnlySet<string> Agreements = new HashSet<string> { "TVöD", "TV-L" };
}
