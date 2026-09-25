using PrivatePrep.Services.Agent;

namespace PrivatePrep.Services.FactGate;

public interface IFactGateService
{
    /// <summary>
    /// Checks one blob of generated text against the CV and story. Used for the narrative parts of a
    /// report (role summary, warnings, verdict, section findings, action plan), which stand or fall
    /// together: if the model invented something there, the whole narrative is suspect.
    /// </summary>
    FactGateResult Verify(string generatedOutput, FactGateContext context);

    /// <summary>
    /// Checks a single bullet rewrite on its own, so one bad rewrite no longer discards the others.
    /// <paramref name="evidenceLine"/> is the CV line the model says the rewrite rests on; it is only
    /// treated as an anchor once it is shown to actually occur in the CV or story. A rewrite whose
    /// claimed evidence is nowhere in the sources fails, otherwise the model could authorise its own
    /// invention just by asserting it.
    /// </summary>
    FactGateBulletResult VerifyBullet(
        BulletRewriteSuggestion bullet,
        string? evidenceLine,
        FactGateContext context);
}

public record FactGateContext(
    string CvText,
    string StoryText,
    IReadOnlyList<string> AllowedSkills);

public record FactGateResult(
    bool Passed,
    IReadOnlyList<FactGateViolation> Violations);

/// <summary>Outcome of checking one bullet rewrite in isolation.</summary>
public record FactGateBulletResult(
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

    /// <summary>The model named a CV line as its evidence, but that line is not in the CV or story.</summary>
    public const string UngroundedEvidence = "ungrounded_evidence";
}
