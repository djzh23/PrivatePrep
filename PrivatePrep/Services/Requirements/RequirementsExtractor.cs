using System.Text.RegularExpressions;

namespace PrivatePrep.Services.Requirements;

public enum RequirementKind
{
    Abschluss,
    Berufserfahrung,
    Fuehrerschein,
    Schicht,
    Sprache,
    Zertifikat,
    Arbeitszeit,
    Einsatzort,
}

/// <param name="Quote">Verbatim excerpt of the posting the requirement comes from (evidence, spec R1).</param>
public sealed record ExtractedRequirement(RequirementKind Kind, string Quote);

public interface IRequirementsExtractor
{
    IReadOnlyList<ExtractedRequirement> Extract(string? postingText);
}

/// <summary>
/// Finds the hard requirements of a job posting without a language model. A keyword only counts inside
/// a requirements section ("Ihr Profil:", "Voraussetzungen:", ...) or in a sentence that signals an
/// obligation ("... setzen wir voraus"). Tasks, offers, optional wishes ("wünschenswert", "von Vorteil")
/// and negations ("nicht erforderlich") are skipped. Working hours are recognised as phrases anywhere;
/// shift work and place of work also count outside a requirements section when the posting names them
/// nowhere else.
/// </summary>
public sealed partial class RequirementsExtractor : IRequirementsExtractor
{
    private enum Section
    {
        None,
        Requirements,
        Other,
    }

    private readonly record struct Candidate(RequirementKind Kind, int Line, int ClauseIndex, int Start, int End);

    private const string RequirementLabels =
        "anforderungen|anforderungsprofil|ihr profil|dein profil|profil|voraussetzungen|das bringen sie mit|sie bringen mit"
        + "|was sie mitbringen|was du mitbringst|das solltest du mitbringen|du solltest haben|sie sollten haben|du bringst mit"
        + "|was wir erwarten|wir erwarten|ihre qualifikationen|qualifikationen|requirements|your profile|what you bring"
        + "|what you'll bring|what you'll need|what you need|you should have|qualifications|who you are|must-haves?"
        + "|must haves?|muss-kriterien|musskriterien|pflichtkriterien|what we're looking for|required|essential";

    private const string OtherLabels =
        "wer wir sind|über uns|about us|(?:ihre |deine |die )?aufgaben(?:gebiet)?|ihr aufgabengebiet|tätigkeiten|ihre tätigkeiten"
        + "|responsibilities|your tasks|what you'll do|wir bieten|das bieten wir|was wir bieten|was wir ihnen bieten|was wir dir bieten"
        + "|was sie erwartet|was dich erwartet|unser angebot|benefits|what we offer|we offer|das erwartet sie|ihre vorteile"
        + "|nice-to-haves?|nice to haves?|wünschenswert";

    /// <summary>A heading ends with a colon or is a line of its own.</summary>
    private const string HeadingEnd = @")[ \t]*(?::[ \t]*|(?=[ \t]*\r?$))";

    private static readonly (RequirementKind Kind, Regex Pattern)[] KindPatterns =
    [
        (RequirementKind.Abschluss, AbschlussWords()),
        (RequirementKind.Berufserfahrung, ErfahrungWords()),
        (RequirementKind.Fuehrerschein, FuehrerscheinWords()),
        (RequirementKind.Schicht, SchichtWords()),
        (RequirementKind.Sprache, SpracheWords()),
        (RequirementKind.Zertifikat, ZertifikatWords()),
        (RequirementKind.Einsatzort, EinsatzortWords()),
    ];

    private static readonly (Section Section, Regex Pattern)[] Headings =
    [
        (Section.Requirements, RequirementHeading()),
        (Section.Other, OtherHeading()),
        (Section.Other, OfferWithoutColon()),
    ];

    /// <summary>A comma followed by one of these words continues the same clause.</summary>
    private static readonly HashSet<string> CommaContinuations =
    [
        "ihn", "sie", "es", "diese", "dies", "dieses", "diesen", "um", "zu", "wo", "was", "welche", "welcher",
        "welches", "dass", "schwerpunkt", "insbesondere", "vorzugsweise", "bzw",
    ];

    /// <summary>Abbreviations that end in a dot without ending the sentence. Dotted initials ("z. B.") are handled separately.</summary>
    private static readonly HashSet<string> Abbreviations =
    [
        "ca", "bzw", "ggf", "inkl", "max", "min", "dr", "prof", "ff", "evtl", "usw", "etc", "mr", "mrs", "ms", "vs",
        "incl", "approx",
    ];

    /// <summary>"1. Oktober": a dot after a day number is not the end of a sentence.</summary>
    private static readonly HashSet<string> Months =
    [
        "januar", "februar", "märz", "april", "mai", "juni", "juli", "august", "september", "oktober", "november",
        "dezember", "jan", "feb", "mär", "apr", "jun", "jul", "aug", "sep", "sept", "okt", "nov", "dez",
    ];

    /// <summary>"... und bringen ..." starts a new clause when a finite verb follows.</summary>
    private static readonly HashSet<string> AndVerbs =
    [
        "bringen", "verfügen", "haben", "besitzen", "sind", "sprechen", "können", "kennen", "beherrschen",
        "erwarten", "benötigen", "arbeiten",
    ];

    public IReadOnlyList<ExtractedRequirement> Extract(string? postingText)
    {
        if (string.IsNullOrWhiteSpace(postingText))
            return [];

        var text = postingText;
        var candidates = new List<Candidate>();
        var loose = new List<Candidate>();
        var section = Section.None;
        var clauseIndex = 0;
        var lineNumber = 0;

        foreach (var (lineStart, lineEnd) in Lines(text))
        {
            lineNumber++;
            var carriedContext = false; // an enumeration keeps the context of its first item until the sentence ends
            if (text.AsSpan(lineStart, lineEnd - lineStart).IsWhiteSpace())
            {
                section = Section.None;
                continue;
            }

            var bodyStart = lineStart + BulletLength(text, lineStart, lineEnd);
            var effective = section;
            var sectionAfterLine = section;

            if (MatchHeading(text, bodyStart, lineEnd) is var (headingSection, headingBodyStart))
            {
                bodyStart = headingBodyStart;
                if (text.AsSpan(bodyStart, lineEnd - bodyStart).IsWhiteSpace())
                {
                    // A heading on its own line governs the following lines.
                    section = headingSection;
                    continue;
                }

                effective = headingSection;
                sectionAfterLine = Section.None;
            }

            if (effective != Section.Other)
            {
                foreach (var (clauseStart, clauseEnd) in SplitClauses(text, bodyStart, lineEnd))
                {
                    var (start, end) = Tidy(text, clauseStart, clauseEnd);
                    clauseIndex++;
                    if (end <= start)
                        continue;

                    // The cue and the wish/negation markers are read from the whole clause; tidying may
                    // have cut a lead-in such as "Sie verfügen über" that carries the cue.
                    var whole = text.Substring(clauseStart, clauseEnd - clauseStart);
                    if (OptionalOrNegated().IsMatch(Parenthetical().Replace(whole, " ")))
                        continue;

                    var hasCue = ObligationCue().IsMatch(whole);
                    var inContext = effective == Section.Requirements || hasCue || carriedContext;
                    carriedContext = (hasCue || carriedContext) && !(clauseEnd < lineEnd && text[clauseEnd] == '.');
                    var clause = text.Substring(start, end - start);
                    foreach (var (kind, pattern) in KindPatterns)
                    {
                        if (!pattern.IsMatch(clause))
                            continue;

                        var candidate = new Candidate(kind, lineNumber, clauseIndex, start, end);
                        if (inContext)
                            candidates.Add(candidate);
                        else if (kind == RequirementKind.Schicht || (kind == RequirementKind.Einsatzort && LoosePlaceOfWork().IsMatch(clause)))
                            loose.Add(candidate);
                    }
                }
            }

            section = sectionAfterLine;
        }

        // Shift work and place of work are working conditions: when no requirement names them, a plain mention counts.
        // The title line names the role, not the conditions: prefer the body when it mentions them.
        foreach (var group in loose.GroupBy(l => l.Kind))
        {
            if (candidates.Exists(c => c.Kind == group.Key))
                continue;

            var body = group.Where(l => l.Line > 1).ToList();
            candidates.AddRange(body.Count > 0 ? body : group);
        }

        var found = MergeNeighbours(text, candidates);
        var hours = WorkingHours().Matches(text).Select(m => (Start: m.Index, End: m.Index + m.Length)).ToList();
        foreach (var h in hours.Where(h => !hours.Exists(o => o != h && o.Start <= h.Start && o.End >= h.End)))
            found.Add((RequirementKind.Arbeitszeit, h.Start, h.End));

        return found
            .OrderBy(f => f.Start)
            .Select(f => new ExtractedRequirement(f.Kind, text[f.Start..f.End]))
            .DistinctBy(r => (r.Kind, r.Quote.ToLowerInvariant()))
            .ToList();
    }

    /// <summary>Clauses of the same kind that are only separated by a comma ("Fließend Englisch, gutes Deutsch") become one quote.</summary>
    private static List<(RequirementKind Kind, int Start, int End)> MergeNeighbours(string text, List<Candidate> candidates)
    {
        var merged = new List<(RequirementKind Kind, int Start, int End)>();
        foreach (var group in candidates.GroupBy(c => c.Kind))
        {
            Candidate? previous = null;
            var start = 0;
            var end = 0;
            foreach (var c in group.OrderBy(c => c.ClauseIndex))
            {
                if (previous is { } p && c.Line == p.Line && c.ClauseIndex == p.ClauseIndex + 1
                    && OnlyACommaBetween(text, end, c.Start))
                {
                    end = c.End;
                }
                else
                {
                    if (previous is not null)
                        merged.Add((group.Key, start, end));
                    start = c.Start;
                    end = c.End;
                }

                previous = c;
            }

            if (previous is not null)
                merged.Add((group.Key, start, end));
        }

        return merged;
    }

    private static bool OnlyACommaBetween(string text, int from, int to) =>
        to >= from && text.AsSpan(from, to - from).Trim().SequenceEqual(",");

    private static IEnumerable<(int Start, int End)> Lines(string text)
    {
        var start = 0;
        while (start <= text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline;
            yield return (start, end);
            if (newline < 0)
                yield break;
            start = newline + 1;
        }
    }

    private static int BulletLength(string text, int lineStart, int lineEnd)
    {
        var match = Bullet().Match(text, lineStart);
        return match.Success && match.Index == lineStart && match.Index + match.Length <= lineEnd ? match.Length : 0;
    }

    private static (Section Section, int BodyStart)? MatchHeading(string text, int at, int lineEnd)
    {
        foreach (var (section, pattern) in Headings)
        {
            var match = pattern.Match(text, at);
            if (match.Success && match.Index == at && match.Index + match.Length <= lineEnd)
                return (section, match.Index + match.Length);
        }

        return null;
    }

    private static List<(int Start, int End)> SplitClauses(string text, int from, int to)
    {
        var clauses = new List<(int, int)>();
        var start = from;
        var depth = 0;
        for (var i = from; i < to; i++)
        {
            var c = text[i];
            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                if (depth > 0)
                    depth--;
                continue;
            }

            if (depth > 0)
                continue;

            var boundary = c switch
            {
                ';' or '•' or '·' or '▪' => true,
                ',' => IsClauseComma(text, i, to),
                '.' => IsSentenceEnd(text, i, to),
                ' ' => IsAndBeforeVerb(text, i, to) || IsOptionalLead(text, i, to),
                _ => false,
            };
            if (!boundary)
                continue;

            clauses.Add((start, i));
            start = i + 1;
        }

        clauses.Add((start, to));
        return clauses;
    }

    private static bool IsClauseComma(string text, int i, int to)
    {
        var j = i + 1;
        while (j < to && char.IsWhiteSpace(text[j]))
            j++;
        if (j >= to)
            return true;
        if (char.IsDigit(text[j]) && ClockRange().IsMatch(text.AsSpan(j, to - j)))
            return false;
        return !CommaContinuations.Contains(NextWord(text, j, to).ToLowerInvariant());
    }

    private static bool IsSentenceEnd(string text, int i, int to)
    {
        if (i + 1 >= to)
            return true;
        if (!char.IsWhiteSpace(text[i + 1]))
            return false;

        var k = i;
        while (k > 0 && char.IsLetter(text[k - 1]))
            k--;
        var previous = text[k..i].ToLowerInvariant();
        if (Abbreviations.Contains(previous))
            return false;

        var j = i + 1;
        while (j < to && char.IsWhiteSpace(text[j]))
            j++;
        if (j >= to)
            return true;

        // Dotted initials such as "z. B." or "d. h.": either the first dot (another initial follows) or the second one.
        if (previous.Length == 1)
        {
            if (j + 1 < to && char.IsLetter(text[j]) && text[j + 1] == '.')
                return false;
            if (k >= 3 && text[k - 1] == ' ' && text[k - 2] == '.' && char.IsLetter(text[k - 3]))
                return false;
        }

        if (i > 0 && char.IsDigit(text[i - 1]) && Months.Contains(NextWord(text, j, to).ToLowerInvariant()))
            return false;
        return !char.IsLower(text[j]);
    }

    private static bool IsAndBeforeVerb(string text, int i, int to)
    {
        if (i + 5 > to || string.Compare(text, i + 1, "und ", 0, 4, StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        return AndVerbs.Contains(NextWord(text, i + 5, to).ToLowerInvariant());
    }

    /// <summary>"..., idealerweise X" starts an optional wish that must not swallow the requirement before it.</summary>
    private static bool IsOptionalLead(string text, int i, int to) =>
        NextWord(text, i + 1, to).ToLowerInvariant() is "idealerweise" or "ideally";

    private static string NextWord(string text, int from, int to)
    {
        var k = from;
        while (k < to && char.IsLetter(text[k]))
            k++;
        return text[from..k];
    }

    /// <summary>Trims a clause to its content: no padding, leading conjunction, lead-in phrase or closing punctuation.</summary>
    private static (int Start, int End) Tidy(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        while (end > start && (char.IsWhiteSpace(text[end - 1]) || text[end - 1] is '.' or ',' or ';' or ':'))
            end--;
        if (end <= start)
            return (start, start);

        var conjunction = LeadingConjunction().Match(text, start, end - start);
        if (conjunction.Success)
            start += conjunction.Length;

        var leadIn = LeadIn().Match(text, start, end - start);
        if (leadIn.Success)
        {
            start += leadIn.Length;
            var trailing = TrailingParticle().Match(text, start, end - start);
            if (trailing.Success)
                end = trailing.Index;
        }

        return (start, end);
    }

    // ---- structure ---------------------------------------------------------------------------

    [GeneratedRegex(@"\G[ \t]*(?:[-–—*•·▪‣◦]|\d{1,2}[.)])[ \t]+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\G[ \t]*(?:" + RequirementLabels + HeadingEnd,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex RequirementHeading();

    [GeneratedRegex(@"\G[ \t]*(?:" + OtherLabels + HeadingEnd,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex OtherHeading();

    [GeneratedRegex(@"\G[ \t]*(?:wir bieten|das bieten wir|was wir bieten|what we offer|we offer)\b[ \t]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OfferWithoutColon();

    [GeneratedRegex(@"^\d{1,2}(?::\d{2})?\s*(?:bis|-|–)\s*\d{1,2}(?::\d{2})?\s*uhr", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockRange();

    [GeneratedRegex(@"^(?:und|oder|sowie|and|or)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingConjunction();

    [GeneratedRegex(@"^(?:wir\s+setzen\s+(?:den|die|das)\s+|sie\s+verfügen\s+über\s+(?:die|den|das|eine|einen|ein)\s+|sie\s+haben\s+(?:die|den|das|eine|einen|ein)\s+|wir\s+erwarten\s+|sie\s+bringen\s+|voraussetzung(?:en)?\s+(?:ist|sind)\s+(?:die|der|das|eine|einen|ein)\s+|you\s+bring\s+|you\s+have\s+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadIn();

    [GeneratedRegex(@"\s+(?:voraus|mit)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingParticle();

    // ---- what a clause says --------------------------------------------------------------------

    [GeneratedRegex(@"\b(?:wünschenswert\w*|von\s+vorteil|vorteilhaft|idealerweise|ideal\s+wäre|gern(?:e)?\s+gesehen|erwünscht|ein\s+plus|is\s+a\s+plus|are\s+a\s+plus|nice[- ]to[- ]have|preferred|bonus\s+points|advantageous|desirable|optional|wäre\s+schön|wünschen\s+wir\s+uns)\b|\b(?:nicht|kein\w*|not|no)\b[^.,;]{0,40}?\b(?:erforderlich|notwendig|nötig|zwingend|required|necessary|needed)\b|\b(?:erforderlich|notwendig|nötig|required|necessary)\b\s+(?:nicht|not)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OptionalOrNegated();

    /// <summary>Signals that a sentence describes what the candidate has to bring: obligation words and address forms.</summary>
    [GeneratedRegex(@"\b(?:voraussetz\w*|erforderlich|zwingend|voraus|verfügen\s+über|sie\s+bringen|bringen\s+sie|erwarten|mindestens|abgeschlossen\w*|muss|müssen|must|required|at\s+least|nachweis\w*|vorzulegen|benötig\w*|vorausgesetzt|besitzen|besitzt|suchen\s+wir|wir\s+suchen|looking\s+for|sie|du|dich|dir|you|your|candidate|kandidat\w*|bewerber\w*|should|sollten|solltest|hast|haben|hold|holds|studierst|studiert)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObligationCue();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"\b(?:\w*ausbildung|\w*weiterbildung|abgeschlossen\w*|examiniert\w*|studier\w*|staatlich\s+anerkannt\w*|hochschulabschluss|studium|studiengang|hochschulstudium|examen|staatsexamen|gesellenbrief|gesellenprüfung|facharbeiter\w*|meister(?:in|brief\w*|prüfung)?|meisterprüfung|befähigungsnachweis|approbation|staatliche\s+anerkennung|anerkennung\s+als|hauptschulabschluss|realschulabschluss|mittlere\s+reife|abitur|fachabitur|diplom\w*|diploma|bachelor\w*|master\w*|qualifikation\w*|immatrikuliert|verwaltungslehrgang\w*|berufsabschluss|degree|(?:commercial|vocational|professional|technical)\s+training|apprenticeship|qualification\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbschlussWords();

    [GeneratedRegex(@"(?<!gleichwertige\s)(?<!vergleichbare\s)(?<!equivalent\s)\b(?:\w*erfahrung\w*|experience)\b|\b(?:mindestens|at\s+least|minimum(?:\s+of)?)\s+(?:\d+|ein|eine|einem|einen|zwei|drei|vier|fünf|sechs)\+?\s+(?:jahr\w*|years?)\b|\b\d+\+\s*years?\b|\b\d+\s*[-–]\s*\d+\s+(?:jahre\w*|years?)\b|\b\d+\s+(?:jahre\w*|years?)\s+(?:in|as|of|als|im|bei)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErfahrungWords();

    [GeneratedRegex(@"\b(?:führerschein\w*|fahrerlaubnis\w*|driver['’]?s?\s+licen[cs]e|driving\s+licen[cs]e)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FuehrerscheinWords();

    [GeneratedRegex(@"\b(?:schicht\w*|\w+-schicht\w*|(?:früh|spät|nacht|wechsel)schicht\w*|(?:früh|spät|nacht)dienst\w*|nacht(?:stunden|wache|bereitschaft|arbeit)\w*|shift\w*|wochenend\w*|samstag\w*|sonntag\w*|bereitschaftsdienst\w*|rufbereitschaft\w*|einsatzzeiten|am\s+abend|abend(?:s|termine|dienst)|feiertag\w*|on-?call|dienstplan\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchichtWords();

    [GeneratedRegex(@"\b(?:deutsch(?!land)\w*|englisch\w*|german(?!y)|english|französisch\w*|spanisch\w*|türkisch\w*|russisch\w*|arabisch\w*|polnisch\w*|italienisch\w*|sprachkenntnis\w*|muttersprach\w*|fließend\w*|verhandlungssicher\w*|fluent|native|niveau\s+[abc][12]|[abc][12])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpracheWords();

    [GeneratedRegex(@"\b(?:zertifikat\w*|zertifizierung\w*|führungszeugnis\w*|gewerbeschein\w*|nachweis\w*|aevo\w*|sgb|fachkunde\w*|lizenz\w*|zulassung\w*|masernschutz|impfnachweis|gesundheitszeugnis|sachkundenachweis|\w*belehrung|ifsg|\w*urkunde|kassenzulassung|certificat\w*|certified|permit)\b|(?<!driver['’]?s?\s)(?<!driving\s)\blicen[cs]e\b|§\s?\d+\w*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZertifikatWords();

    [GeneratedRegex(@"\b(?:reisebereitschaft\w*|reisetätigkeit\w*|montageeinsätze\w*|montageeinsatz\w*|einsatz(?:ort|gebiet)\w*|umkreis\w*|präsenz\w*|homeoffice|home-office|remote|hybrid|willingness\s+to\s+travel|travel\s+up\s+to|on-?site|vor\s+ort|pendel\w*|mobilität\w*|dienstreis\w*|travel(?:l?ing|s)?|reisen|im\s+büro|in\s+the\s+office)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EinsatzortWords();

    /// <summary>Stricter than <see cref="EinsatzortWords"/>: outside a requirements section "hybrid" alone is too ambiguous ("hybrid infrastructure").</summary>
    [GeneratedRegex(@"\b(?:remote|homeoffice|home-office|on-?site|vor\s+ort|reisebereitschaft\w*|willingness\s+to\s+travel|präsenz\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoosePlaceOfWork();

    /// <summary>Working-time phrases are recognised anywhere, also in the title line.</summary>
    [GeneratedRegex(@"(?:\b(?:in|im|als)\s+)?\b(?:vollzeit\s+oder\s+teilzeit|teilzeit\s+oder\s+vollzeit|(?:vollzeit|teilzeit)(?:\s*\(\s*\d{1,2}(?:[.,]\d)?\s*(?:stunden|std\.?)(?:\s+(?:pro|die|in\s+der)\s+woche)?\s*\)|\s+(?:mit\s+)?\d{1,2}(?:[.,]\d)?\s*(?:stunden|std\.?)(?:\s+(?:pro|die|in\s+der)\s+woche)?\b|\b))"
        + @"|\b\d{1,2}(?:[.,]\d)?\s*wochenstunden\b"
        + @"|\bminijob(?:,\s*\d{1,2}\s*stunden\s+pro\s+woche)?"
        + @"|\b\d{1,2}\s*(?:bis|-|–)\s*\d{1,2}\s*(?:stunden|std\.?)(?:\s+(?:pro|die|in\s+der)\s+woche)?\b"
        + @"|(?:\b(?:maximal|höchstens|bis\s+zu|ca\.?|rund)\s+)?\b\d{1,2}(?:[.,]\d)?\s*(?:stunden|std\.?)\s+(?:pro|die|in\s+der)\s+woche\b"
        + @"|\b(?:1[5-9]|[2-4]\d)\s+stunden\b(?!\s+(?:am|pro|je|im)\s+(?:tag|monat|jahr))"
        + @"|(?<=\),\s)\d{2,3}\s*(?:prozent|%)|(?<=\bstelle\s)\d{2,3}\s*(?:prozent|%)|(?<=umfang\s)(?:von\s)?\d{2,3}\s*(?:prozent|%)"
        + @"|\bmontag\s*(?:bis|-|–)\s*(?:freitag|donnerstag|samstag)(?:,?\s*\d{1,2}(?::\d{2})?\s*(?:bis|-|–)\s*\d{1,2}(?::\d{2})?\s*uhr)?"
        + @"|\bfull[- ]?time\b"
        + @"|\bpart[- ]?time(?:\s*\(\s*\d{1,2}\s*hours?(?:\s+(?:per|a)\s+week)?\s*\)|\s+\d{1,2}\s*hours?\b)?"
        + @"|\b\d{1,2}\s*hours?\s+(?:per|a)\s+week\b"
        + @"|\bmonday\s+to\s+\w+(?:\s+from\s+\d{1,2}\s*(?:am|pm)?\s+to\s+\d{1,2}\s*(?:am|pm))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WorkingHours();
}
