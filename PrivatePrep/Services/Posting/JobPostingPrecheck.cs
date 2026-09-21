using System.Text.RegularExpressions;
using PrivatePrep.Services.Agent;

namespace PrivatePrep.Services.Posting;

public enum PostingLanguage
{
    Unknown,
    German,
    English,
}

/// <param name="IsJobPosting">The text shows enough typical posting features (see <see cref="JobPostingPrecheck.MinimumSignals"/>).</param>
/// <param name="Truncated">The input exceeded the length limit and <paramref name="Text"/> is its beginning.</param>
/// <param name="Text">Trimmed input, cut at a word boundary when it was too long. Everything else works on this text.</param>
/// <param name="OriginalLength">Length of the trimmed input before cutting.</param>
/// <param name="Signals">Names of the posting features that were found.</param>
public sealed record PostingPrecheckResult(
    bool IsJobPosting,
    PostingLanguage Language,
    bool Truncated,
    string Text,
    int OriginalLength,
    IReadOnlyList<string> Signals);

public interface IJobPostingPrecheck
{
    PostingPrecheckResult Check(string? text);
}

/// <summary>
/// Cheap first look at the pasted text, without a language model: is it a job posting at all,
/// which language is it in, and does it have to be shortened? Rejecting non-postings here keeps the
/// model call (and the user's daily attempt) for input that deserves an analysis.
/// </summary>
public sealed partial class JobPostingPrecheck : IJobPostingPrecheck
{
    /// <summary>Distinct kinds of posting features (of seven) required before a text counts as a posting.</summary>
    public const int MinimumSignals = 3;

    private const int MinimumLanguageWords = 3;
    private const double LanguageDominance = 1.5;
    private const double BoundaryWindow = 0.9;

    private static readonly System.Buffers.SearchValues<char> WordBreaks = System.Buffers.SearchValues.Create(" \n\r\t");

    private static readonly (string Name, Regex Pattern)[] SignalPatterns =
    [
        ("role_marker", RoleMarker()),
        ("section_heading", SectionHeading()),
        ("employment_terms", EmploymentTerms()),
        ("hiring_intent", HiringIntent()),
        ("application", Application()),
        ("compensation", Compensation()),
        ("requirement_wording", RequirementWording()),
    ];

    private static readonly HashSet<string> GermanWords =
    [
        "der", "die", "das", "den", "dem", "des", "und", "oder", "ist", "sind", "wir", "sie", "ihr", "ihre",
        "ihren", "ihnen", "für", "mit", "von", "zu", "zum", "zur", "im", "auf", "bei", "ein", "eine", "einen",
        "einer", "nicht", "auch", "als", "wird", "werden", "sich", "über", "nach", "aus", "bis", "unser",
        "unsere", "unserem", "uns", "es", "hat", "haben", "sowie", "bitte",
    ];

    private static readonly HashSet<string> EnglishWords =
    [
        "the", "and", "or", "is", "are", "was", "we", "you", "your", "our", "for", "with", "of", "to", "on",
        "at", "a", "will", "be", "as", "by", "from", "this", "that", "have", "has", "not", "it", "their",
        "they", "if", "can",
    ];

    public PostingPrecheckResult Check(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
            return new PostingPrecheckResult(false, PostingLanguage.Unknown, false, "", 0, []);

        var truncated = trimmed.Length > AnalyzeService.MaxJobDescriptionLength;
        var body = truncated ? CutAtBoundary(trimmed, AnalyzeService.MaxJobDescriptionLength) : trimmed;

        var signals = SignalPatterns
            .Where(s => s.Pattern.IsMatch(body))
            .Select(s => s.Name)
            .ToList();

        return new PostingPrecheckResult(
            signals.Count >= MinimumSignals,
            DetectLanguage(body),
            truncated,
            body,
            trimmed.Length,
            signals);
    }

    private static string CutAtBoundary(string text, int max)
    {
        if (char.IsWhiteSpace(text[max]))
            return text[..max].TrimEnd();

        var boundary = text.AsSpan(0, max).LastIndexOfAny(WordBreaks);
        return boundary > max * BoundaryWindow ? text[..boundary].TrimEnd() : text[..max].TrimEnd();
    }

    private static PostingLanguage DetectLanguage(string text)
    {
        var german = 0;
        var english = 0;
        foreach (var match in Words().EnumerateMatches(text))
        {
            var word = text.AsSpan(match.Index, match.Length).ToString().ToLowerInvariant();
            if (GermanWords.Contains(word))
                german++;
            else if (EnglishWords.Contains(word))
                english++;
        }

        if (german + english < MinimumLanguageWords)
            return PostingLanguage.Unknown;
        if (german >= english * LanguageDominance)
            return PostingLanguage.German;
        if (english >= german * LanguageDominance)
            return PostingLanguage.English;
        return PostingLanguage.Unknown;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();

    // Gender marker such as (m/w/d), (m/f/d), or a posting label.
    [GeneratedRegex(@"\b[mwfdx]\s?/\s?[mwfdx]\s?/\s?[mwfdx]\b|\ball genders\b|\(gn\)|\b(stellenangebot|stellenanzeige|job description|job posting)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleMarker();

    [GeneratedRegex(@"\b(aufgaben|ihr profil|dein profil|anforderungen|voraussetzungen|qualifikationen|wir bieten|das bieten wir|wir erwarten|was wir erwarten|das bringen sie mit|was sie mitbringen|das solltest du mitbringen|responsibilities|requirements|qualifications|your profile|what you bring|what we offer|we offer|your tasks)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"\b(vollzeit|teilzeit|unbefristet|befristet|minijob|werkstudent(in)?|ausbildung|berufserfahrung|berufseinsteiger|festanstellung|praktikum|full[- ]?time|part[- ]?time|years? of experience|jahre erfahrung|stelle|position|vacancy|trainee(programm)?|apprenticeship)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmploymentTerms();

    [GeneratedRegex(@"\b(wir suchen|suchen wir|sucht|gesucht|zu besetzen|besetzen|nächstmöglichen zeitpunkt|ab sofort|verstärkung|wir stellen|are looking for|is looking for|we are hiring|we're hiring|join our team|are seeking|is seeking|to join)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiringIntent();

    [GeneratedRegex(@"\b(bewerbung(en)?|bewerben|bewirb|ansprechpartner(in)?|apply|application|contact|kontakt|auskünfte|melden sie sich|melde dich|questions)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Application();

    [GeneratedRegex(@"\b(gehalt|jahresgehalt|vergütung|entgeltgruppe|brutto|urlaubstage|urlaub|salary|gross|vacation|stundenlohn|bonus|provision|honorar|benefits|altersvorsorge|euro|eur)\b|€",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Compensation();

    [GeneratedRegex(@"\b(erforderlich|mindestens|abgeschlossen(e|er|en|em|es)?|verfügen über|bringen sie mit|bringen mit|bringst du mit|required|must|at least|completed|von vorteil|wünschenswert|erwünscht|idealerweise|is a plus|preferred|erfahrung mit|experience with)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequirementWording();
}
