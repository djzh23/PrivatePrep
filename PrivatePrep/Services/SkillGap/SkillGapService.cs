namespace PrivatePrep.Services.SkillGap;

public sealed class SkillGapService : ISkillGapService
{
    public const int MinimumJobDescriptionLength = 100;

    private static readonly string[] RequirementHeaders =
    [
        "technische anforderungen",
        "fachliche anforderungen",
        "persönliche anforderungen",
        "anforderungsprofil",
        "voraussetzungen",
        "das bringen sie mit",
        "das erwarten wir",
        "sie verfügen über",
        "what we're looking for",
        "what we are looking for",
        "what you'll bring",
        "what you will bring",
        "was sie mitbringen",
        "was du mitbringst",
        "sie bringen mit",
        "du bringst mit",
        "skills and experience",
        "your background",
        "your experience",
        "your profile",
        "you will have",
        "nice-to-have",
        "nice to have",
        "must-have",
        "must have",
        "anforderungen",
        "requirements",
        "qualifications",
        "ihr profil",
        "dein profil",
        "wir erwarten",
        "erwartungen",
        "wünschenswert",
        "idealerweise",
        "pluspunkte",
        "who you are",
        "about you",
        "you have",
        "required",
        "you bring",
    ];

    private static readonly string[] RequirementEndHeaders =
    [
        "about the company",
        "about the team",
        "about the role",
        "equal opportunity",
        "how to apply",
        "what we offer",
        "your responsibilities",
        "deine aufgaben",
        "ihre aufgaben",
        "stellenbeschreibung",
        "unser angebot",
        "das bieten wir",
        "wir bieten",
        "vergütung",
        "compensation",
        "benefits",
        "gehalt",
        "salary",
        "über uns",
        "about us",
        "aufgaben",
        "perks",
        "why join",
    ];

    private static readonly string[] CvSkillHeaders =
    [
        "technische kenntnisse",
        "technical skills",
        "it-kenntnisse",
        "it kenntnisse",
        "edv-kenntnisse",
        "edv kenntnisse",
        "softwarekenntnisse",
        "fachkenntnisse",
        "hard skills",
        "tech-stack",
        "tech stack",
        "technologien",
        "kenntnisse",
        "fähigkeiten",
        "kompetenzen",
        "tool stack",
        "skills",
        "tools",
    ];

    private static readonly string[] CvSkillEndHeaders =
    [
        "professional experience",
        "berufliche erfahrung",
        "beruflicher werdegang",
        "professional summary",
        "work experience",
        "zertifikate",
        "certificates",
        "certifications",
        "fortbildung",
        "ausbildung",
        "berufserfahrung",
        "tätigkeiten",
        "experience",
        "education",
        "projekte",
        "projects",
        "sprachen",
        "languages",
        "über mich",
        "about me",
        "werdegang",
        "studium",
        "summary",
        "profil",
    ];

    private readonly SkillTaxonomyCatalog _taxonomy;

    public SkillGapService()
        : this(SkillTaxonomyCatalog.LoadEmbedded())
    {
    }

    public SkillGapService(SkillTaxonomyCatalog taxonomy)
    {
        ArgumentNullException.ThrowIfNull(taxonomy);
        _taxonomy = taxonomy;
    }

    public SkillGapReport Classify(string cvText, string jobDescription)
    {
        cvText ??= string.Empty;
        jobDescription ??= string.Empty;

        if (jobDescription.Trim().Length < MinimumJobDescriptionLength)
            return Empty(SkillGapReasonCodes.EmptyJd);

        var requirements = TrySliceSection(jobDescription, RequirementHeaders, RequirementEndHeaders);
        if (requirements is null)
            return Empty(SkillGapReasonCodes.NoRequirementsSection);

        var extracted = ExtractSkills(requirements);
        if (extracted.Count == 0)
            return Empty(SkillGapReasonCodes.NoSkillCandidates);

        var (skillsSection, prose) = SplitCv(cvText);
        var existing = new List<string>();
        var supported = new List<string>();
        var gap = new List<string>();

        foreach (var name in extracted)
        {
            if (!_taxonomy.TryGet(name, out var skill))
            {
                gap.Add(name);
                continue;
            }

            if (_taxonomy.MentionedInCv(skillsSection, skill))
                existing.Add(skill.Name);
            else if (_taxonomy.MentionedInCv(prose, skill))
                supported.Add(skill.Name);
            else
                gap.Add(skill.Name);
        }

        return new SkillGapReport(existing, supported, gap, extracted, SkillGapReasonCodes.Ok);
    }

    private List<string> ExtractSkills(string requirementsText)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in _taxonomy.Skills)
        {
            if (!_taxonomy.MatchesDirect(requirementsText, skill) || !seen.Add(skill.Name))
                continue;

            found.Add(skill.Name);
            foreach (var implied in skill.Implies)
            {
                if (seen.Add(implied))
                    found.Add(implied);
            }
        }

        return found;
    }

    private static (string SkillsSection, string Prose) SplitCv(string cvText)
    {
        var skills = TrySliceSection(cvText, CvSkillHeaders, CvSkillEndHeaders);
        if (skills is null)
            return (string.Empty, cvText);

        var idx = IndexOfHeading(cvText, CvSkillHeaders);
        if (idx < 0)
            return (skills, cvText);

        var end = idx + skills.Length;
        var prose = (cvText[..idx] + " " + (end < cvText.Length ? cvText[end..] : string.Empty)).Trim();
        return (skills, prose);
    }

    private static string? TrySliceSection(string text, string[] startHeaders, string[] endHeaders)
    {
        var start = IndexOfHeading(text, startHeaders);
        if (start < 0)
            return null;

        var fromHeading = text[start..];
        var relativeEnd = IndexOfHeading(fromHeading[1..], endHeaders);
        if (relativeEnd < 0)
            return fromHeading;

        return fromHeading[..(relativeEnd + 1)];
    }

    private static int IndexOfHeading(string text, string[] headings)
    {
        var bestIndex = -1;
        var bestLength = -1;

        foreach (var heading in headings)
        {
            var idx = 0;
            while ((idx = text.IndexOf(heading, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if (IsHeadingContext(text, idx, heading.Length) && heading.Length > bestLength)
                {
                    bestIndex = idx;
                    bestLength = heading.Length;
                }

                idx += heading.Length;
            }
        }

        return bestIndex;
    }

    private static bool IsHeadingContext(string text, int index, int length)
    {
        var prefixOk = index == 0
            || char.IsWhiteSpace(text[index - 1])
            || text[index - 1] is '#' or '*' or '-' or '•' or '(';

        var end = index + length;
        var suffixOk = end >= text.Length
            || char.IsWhiteSpace(text[end])
            || text[end] is ':' or '-' or '/' or ')' or ',' or '.';

        return prefixOk && suffixOk;
    }

    private static SkillGapReport Empty(string reasonCode) =>
        new([], [], [], [], reasonCode);
}
