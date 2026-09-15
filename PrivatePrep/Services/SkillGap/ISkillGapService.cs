namespace PrivatePrep.Services.SkillGap;

public interface ISkillGapService
{
    SkillGapReport Classify(string cvText, string jobDescription);
}

public record SkillGapReport(
    IReadOnlyList<string> Existing,
    IReadOnlyList<string> SupportedByResume,
    IReadOnlyList<string> Gap,
    IReadOnlyList<string> ExtractedJdSkills,
    string ReasonCode);

public static class SkillGapReasonCodes
{
    public const string Ok = "ok";
    public const string EmptyJd = "empty-jd";
    public const string NoRequirementsSection = "no-requirements-section";
    public const string NoSkillCandidates = "no-skill-candidates";
}
