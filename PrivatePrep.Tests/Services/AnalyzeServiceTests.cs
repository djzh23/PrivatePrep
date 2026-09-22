using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.FactGate;
using PrivatePrep.Services.Privacy;
using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Tests.Services;

public class AnalyzeServiceTests
{
    private readonly Mock<ISkillGapService> _gap = new();
    private readonly Mock<IFactGateService> _gate = new();
    private readonly Mock<ILlmRouter> _llm = new();

    private AnalyzeService CreateSut() =>
        new(_gap.Object, _gate.Object, _llm.Object, new PiiScrubberService(), NullLogger<AnalyzeService>.Instance);

    private static string SampleCv => """
        Kenntnisse
        C#, ASP.NET Core

        Berufserfahrung
        Entwickelte ASP.NET Core REST APIs für interne Tools.
        """;

    private static string SampleJd =>
        "Anforderungen\nC# und ASP.NET Core für das Backend-Team. "
        + "Die Stelle ist unbefristet und das Team sitzt in Deutschland.";

    private static AnalyzeRequest Request(string? cv = null, string? jd = null, string story = "") =>
        new("user_1", cv ?? SampleCv, story, jd ?? SampleJd);

    private static SkillGapReport OkGap() =>
        new(["C#", "ASP.NET Core"], [], [], ["C#", "ASP.NET Core"], SkillGapReasonCodes.Ok);

    private static string ValidJson(
        decimal global = 3.8m,
        decimal culture = 3.0m,
        string cultureScreen = "caution",
        string rewritten = "Entwickelte ASP.NET Core REST APIs für interne Tools.") =>
        $$"""
        {
          "global_score": {{global.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
          "dimensions": {
            "cv_match": 4.0,
            "role_alignment": 3.5,
            "culture": {{culture.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
            "red_flags": 1.0
          },
          "role_summary": "Junior .NET Backend, Remote",
          "culture_screen": "{{cultureScreen}}",
          "warnings": [],
          "bullet_rewrites": [
            {
              "original": "Entwickelte ASP.NET Core REST APIs für interne Tools.",
              "rewritten": "{{rewritten}}",
              "reasoning": "JD betont Backend-APIs ohne neue Fakten."
            }
          ]
        }
        """;

    private void SetupHappyCollaborators(string json)
    {
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Returns(new FactGateResult(true, []));
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse(json, "llama-3.3-70b-versatile", 100, 80));
    }

    [Fact]
    public async Task Analyze_ShortJd_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AnalyzeException>(() =>
            sut.AnalyzeAsync(Request(jd: "zu kurz"), CancellationToken.None));

        Assert.Equal("jd_too_short", ex.ErrorCode);
        _llm.Verify(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Analyze_ShortJd_UsesTheWordUsersKnowInTheMessage()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AnalyzeException>(() =>
            sut.AnalyzeAsync(Request(jd: "zu kurz"), CancellationToken.None));

        Assert.Equal("Die Stellenanzeige ist zu kurz.", ex.Message);
    }

    [Fact]
    public async Task Analyze_ModelWritesJd_ReportSaysStellenanzeigeInstead()
    {
        var json = ValidJson()
            .Replace("\"role_summary\": \"Junior .NET Backend, Remote\"", "\"role_summary\": \"Junior .NET Backend, laut JD Remote\"")
            .Replace("\"warnings\": []", "\"warnings\": [\"Kein Mentoring in JD erwähnt\", \"Zwei JDs widersprechen sich\"]");
        SetupHappyCollaborators(json);
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("Junior .NET Backend, laut Stellenanzeige Remote", report.RoleSummary);
        Assert.Contains("Kein Mentoring in Stellenanzeige erwähnt", report.Warnings);
        Assert.Contains("Zwei Stellenanzeigen widersprechen sich", report.Warnings);
        Assert.Equal("Stellenanzeige betont Backend-APIs ohne neue Fakten.", Assert.Single(report.Bullets).Reasoning);
    }

    [Fact]
    public async Task Analyze_EmptyCv_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AnalyzeException>(() =>
            sut.AnalyzeAsync(Request(cv: "   "), CancellationToken.None));

        Assert.Equal("profile_incomplete", ex.ErrorCode);
    }

    [Fact]
    public async Task Analyze_ValidJson_ReturnsReportWithBullets()
    {
        SetupHappyCollaborators(ValidJson());
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal(3.8m, report.GlobalScore);
        Assert.Equal("caution", report.CultureScreen);
        Assert.Single(report.Bullets);
        Assert.Empty(report.FactViolations);
        Assert.Equal("llama-3.3-70b-versatile", report.ModelUsed);
    }

    [Fact]
    public async Task Analyze_InvalidJsonThenValidRetry_Succeeds()
    {
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Returns(new FactGateResult(true, []));
        _llm.SetupSequence(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("not json", "llama-3.3-70b-versatile", 10, 10))
            .ReturnsAsync(new LlmResponse(ValidJson(), "llama-3.3-70b-versatile", 12, 20));
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal(3.8m, report.GlobalScore);
        _llm.Verify(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Analyze_InvalidJsonTwice_ThrowsParseFailed()
    {
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("still not json", "llama-3.3-70b-versatile", 10, 10));
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AnalyzeException>(() =>
            sut.AnalyzeAsync(Request(), CancellationToken.None));

        Assert.Equal("llm_parse_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task Analyze_CultureFail_CapsDimensionAndGlobalScore()
    {
        SetupHappyCollaborators(ValidJson(global: 4.6m, culture: 4.2m, cultureScreen: "fail"));
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal(AnalyzeService.CultureFailGlobalCap, report.GlobalScore);
        Assert.Equal(AnalyzeService.CultureFailDimensionCap, report.Dimensions.Culture);
        Assert.Equal("fail", report.CultureScreen);
        Assert.Contains(report.Warnings, w => w.Contains("Culture-Cap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Analyze_FactGateViolation_HardBlocksGeneratedBullets()
    {
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse(
                ValidJson(rewritten: "Betrieb von Kubernetes-Clustern in Produktion."),
                "llama-3.3-70b-versatile",
                100,
                80));
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Returns(new FactGateResult(false, [
                new FactGateViolation(FactGateViolationTypes.InventedSkill, "Kubernetes", "not in CV")
            ]));
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Empty(report.Bullets);
        Assert.Equal("", report.RoleSummary);
        Assert.NotEmpty(report.FactViolations);
        Assert.Contains(report.Warnings, w => w.Contains("FactGate", StringComparison.OrdinalIgnoreCase));

        // The blocked rewrite is not lost: it travels separately so the user can ask to see it anyway.
        var unverified = Assert.Single(report.UnverifiedBullets ?? []);
        Assert.Equal("Betrieb von Kubernetes-Clustern in Produktion.", unverified.RewrittenBullet);
    }

    [Fact]
    public async Task Analyze_ValidJson_HasNoUnverifiedBulletsOnTheHappyPath()
    {
        SetupHappyCollaborators(ValidJson());
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.True(report.UnverifiedBullets is null or []);
    }

    [Fact]
    public async Task Analyze_SystemPrompt_IncludesSkillGapBuckets()
    {
        string? capturedSystem = null;
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SkillGapReport(["C#"], ["Docker"], ["Kubernetes"], ["C#", "Docker", "Kubernetes"], "ok"));
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Returns(new FactGateResult(true, []));
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((system, _, _) => capturedSystem = system)
            .ReturnsAsync(new LlmResponse(ValidJson(), "llama-3.3-70b-versatile", 1, 1));
        var sut = CreateSut();

        await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.NotNull(capturedSystem);
        Assert.Contains("C#", capturedSystem);
        Assert.Contains("Docker", capturedSystem);
        Assert.Contains("Kubernetes", capturedSystem);
    }

    [Fact]
    public async Task Analyze_ScrubsEmailBeforeLlm()
    {
        string? capturedUser = null;
        string? classifiedCv = null;
        SetupHappyCollaborators(ValidJson());
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((cv, _) => classifiedCv = cv)
            .Returns(OkGap());
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, user, _) => capturedUser = user)
            .ReturnsAsync(new LlmResponse(ValidJson(), "llama-3.3-70b-versatile", 1, 1));
        var sut = CreateSut();
        var cv = SampleCv + "\nKontakt: ana@example.com";

        await sut.AnalyzeAsync(Request(cv: cv), CancellationToken.None);

        Assert.DoesNotContain("ana@example.com", capturedUser);
        Assert.DoesNotContain("ana@example.com", classifiedCv);
        Assert.Contains(PiiScrubberService.EmailPlaceholder, capturedUser);
    }
}
