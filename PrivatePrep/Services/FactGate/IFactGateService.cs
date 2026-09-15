namespace PrivatePrep.Services.FactGate;

public interface IFactGateService
{
    FactGateResult Verify(string generatedOutput, FactGateContext context);
}

public record FactGateContext(
    string CvText,
    string StoryText,
    IReadOnlyList<string> AllowedSkills);

public record FactGateResult(
    bool Passed,
    IReadOnlyList<FactGateViolation> Violations);

public record FactGateViolation(
    string ViolationType,
    string Snippet,
    string Reason);

public static class FactGateViolationTypes
{
    public const string InventedSkill = "invented_skill";
    public const string InventedMetric = "invented_metric";
    public const string BannedWord = "banned_word";
}
