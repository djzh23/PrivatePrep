using PrivatePrep.Services.FactGate;

namespace PrivatePrep.Tests.Services;

public class FactGateServiceTests
{
    private readonly FactGateService _sut = new();

    [Fact]
    public void Verify_KubernetesMissingFromSources_IsInventedSkill()
    {
        var context = new FactGateContext(
            CvText: "Kenntnisse: C#, .NET",
            StoryText: "Ich komme aus dem Backend.",
            AllowedSkills: ["C#", ".NET"]);

        var result = _sut.Verify("Für die Rolle bringe ich Kubernetes-Erfahrung mit.", context);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, v =>
            v.ViolationType == FactGateViolationTypes.InventedSkill
            && v.Snippet.Contains("Kubernetes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Verify_UngroundedPercentage_IsInventedMetric()
    {
        var context = new FactGateContext(
            CvText: "Kenntnisse: C#\nBerufserfahrung: APIs entwickelt.",
            StoryText: "",
            AllowedSkills: ["C#"]);

        var result = _sut.Verify("Ich verbesserte Performance um 40% in einem Kundenprojekt.", context);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, v =>
            v.ViolationType == FactGateViolationTypes.InventedMetric
            && v.Snippet.Contains("40%", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Verify_Leidenschaftlich_IsBannedWord()
    {
        var context = new FactGateContext("Kenntnisse: C#", "", ["C#"]);

        var result = _sut.Verify("Ich bin leidenschaftlich bei der Arbeit an APIs.", context);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, v => v.ViolationType == FactGateViolationTypes.BannedWord);
    }

    [Fact]
    public void Verify_CleanRewriteFromCv_Passes()
    {
        var cv = """
            Kenntnisse
            C#, ASP.NET Core, PostgreSQL

            Berufserfahrung
            Entwickelte ASP.NET Core REST APIs für interne Business-Tools mit PostgreSQL-Backend.
            """;
        var context = new FactGateContext(cv, "Ich will im Backend bleiben.", ["C#", "ASP.NET Core", "PostgreSQL"]);
        var output = "Entwickelte ASP.NET Core REST APIs für interne Business-Tools mit PostgreSQL-Backend.";

        var result = _sut.Verify(output, context);

        Assert.True(result.Passed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Verify_YearsOfExperiencePresentInCv_Passes()
    {
        var cv = "Kenntnisse: C#\n3 Jahre Berufserfahrung in der Softwareentwicklung.";
        var context = new FactGateContext(cv, "", ["C#"]);

        var result = _sut.Verify("Der Kandidat hat 3 Jahre Berufserfahrung in der Softwareentwicklung.", context);

        Assert.True(result.Passed);
        Assert.DoesNotContain(result.Violations, v => v.ViolationType == FactGateViolationTypes.InventedMetric);
    }

    [Fact]
    public void Verify_GermanAndEnglishBannedWords_AreBothCaught()
    {
        var context = new FactGateContext("Kenntnisse: C#", "", ["C#"]);

        var german = _sut.Verify("Ich bin leidenschaftlich und ergebnisorientiert.", context);
        var english = _sut.Verify("I delve into problems and leverage C# to ship a robust service.", context);

        Assert.Contains(german.Violations, v => v.ViolationType == FactGateViolationTypes.BannedWord);
        Assert.Contains(english.Violations, v =>
            v.ViolationType == FactGateViolationTypes.BannedWord
            && v.Snippet.Contains("delve", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(english.Violations, v =>
            v.ViolationType == FactGateViolationTypes.BannedWord
            && v.Snippet.Contains("leverage", StringComparison.OrdinalIgnoreCase));
    }
}
