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

    public AnalyzeServiceTests()
    {
        // Per-bullet checks pass unless a test says otherwise, so cases about parsing, scoring or
        // the narrative gate do not each have to restate it.
        _gate
            .Setup(g => g.VerifyBullet(
                It.IsAny<BulletRewriteSuggestion>(),
                It.IsAny<string?>(),
                It.IsAny<FactGateContext>()))
            .Returns(new FactGateBulletResult(true, []));
    }

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

    private static string ValidV2Json(
        decimal global = 3.8m,
        decimal culture = 3.0m,
        string cultureScreen = "caution",
        string verdictHeadline = "Bewerbbar mit gezielten Anpassungen.",
        string verdictParagraph = "Die JD betont Backend-APIs, die im CV belegt sind. Keine harten Lücken.") =>
        $$"""
        {
          "global_score": {{global.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
          "dimensions": {
            "cv_match": 4.0,
            "role_alignment": 3.5,
            "culture": {{culture.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
            "red_flags": 1.0
          },
          "dimension_reasons": {
            "cv_match": "C# und ASP.NET Core sind im CV belegt, wie von der JD verlangt.",
            "role_alignment": "Backend-Fokus passt zur Stelle.",
            "culture": "Team sitzt laut JD in Deutschland.",
            "red_flags": "Keine harten Blocker in der JD gefunden."
          },
          "role_summary": "Junior .NET Backend, Remote",
          "verdict_headline": "{{verdictHeadline}}",
          "verdict_paragraph": "{{verdictParagraph}}",
          "culture_screen": "{{cultureScreen}}",
          "warnings": [],
          "section_findings": [
            {
              "section": "technical_skills",
              "label": "Technische Skills",
              "observation": "C# und ASP.NET Core stehen laut JD an erster Stelle.",
              "action": "Skill-Reihenfolge im CV beibehalten."
            }
          ],
          "action_plan": [
            { "priority": 1, "action": "Bullet bei aktueller Stelle anpassen.", "effort_minutes": 10, "impact": "high" },
            { "priority": 2, "action": "Anschreiben auf Backend-Fokus zuschneiden.", "effort_minutes": null, "impact": "medium" }
          ],
          "bullet_rewrites": [
            {
              "original": "Entwickelte ASP.NET Core REST APIs für interne Tools.",
              "rewritten": "Entwickelte ASP.NET Core REST APIs für interne Tools.",
              "reasoning": "JD betont Backend-APIs ohne neue Fakten.",
              "evidence_line": "Entwickelte ASP.NET Core REST APIs für interne Tools."
            }
          ]
        }
        """;

    [Fact]
    public async Task Analyze_V2Json_ParsesVerdictDimensionReasonsFindingsAndActionPlan()
    {
        SetupHappyCollaborators(ValidV2Json());
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("Bewerbbar mit gezielten Anpassungen.", report.VerdictHeadline);
        Assert.Equal(
            "Die Stellenanzeige betont Backend-APIs, die im CV belegt sind. Keine harten Lücken.",
            report.VerdictParagraph);
        Assert.NotNull(report.DimensionReasons);
        Assert.Equal(
            "C# und ASP.NET Core sind im CV belegt, wie von der Stellenanzeige verlangt.",
            report.DimensionReasons!.CvMatch);
        var finding = Assert.Single(report.SectionFindings ?? []);
        Assert.Equal("technical_skills", finding.Section);
        Assert.Equal(2, (report.ActionPlan ?? []).Count);
        Assert.Equal(1, report.ActionPlan![0].Priority);
        Assert.Equal("high", report.ActionPlan![0].Impact);
        Assert.Equal(
            "Entwickelte ASP.NET Core REST APIs für interne Tools.",
            Assert.Single(report.Bullets).EvidenceLine);
    }

    [Fact]
    public async Task Analyze_V1ShapedJson_LeavesV2FieldsNullOrEmpty()
    {
        SetupHappyCollaborators(ValidJson());
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Null(report.VerdictHeadline);
        Assert.Null(report.VerdictParagraph);
        Assert.Null(report.DimensionReasons);
        Assert.True(report.SectionFindings is null or []);
        Assert.True(report.ActionPlan is null or []);
        Assert.Null(Assert.Single(report.Bullets).EvidenceLine);
    }

    [Fact]
    public async Task Analyze_ActionPlanWithInvalidImpact_NormalizesToMedium()
    {
        var json = ValidV2Json().Replace("\"impact\": \"high\"", "\"impact\": \"dringend\"");
        SetupHappyCollaborators(json);
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("medium", report.ActionPlan![0].Impact);
    }

    [Fact]
    public async Task Analyze_FactGateViolation_SuppressesV2NarrativeFieldsToo()
    {
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse(ValidV2Json(), "llama-3.3-70b-versatile", 100, 80));
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Returns(new FactGateResult(false, [
                new FactGateViolation(FactGateViolationTypes.InventedSkill, "Kubernetes", "not in CV")
            ]));
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Null(report.VerdictHeadline);
        Assert.Null(report.VerdictParagraph);
        Assert.Null(report.DimensionReasons);
        Assert.True(report.SectionFindings is null or []);
        Assert.True(report.ActionPlan is null or []);
    }

    [Fact]
    public async Task Analyze_FactGateSeesV2NarrativeText_NotJustBullets()
    {
        // A verdict_paragraph inventing an unverifiable skill must be caught the same way a bullet
        // rewrite would be: the FactGate input text must include the V2 narrative fields.
        FactGateContext? capturedContext = null;
        string? capturedText = null;
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse(
                ValidV2Json(verdictParagraph: "Kubernetes-Erfahrung macht diesen Kandidaten stark."),
                "llama-3.3-70b-versatile",
                100,
                80));
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Callback<string, FactGateContext>((text, ctx) => { capturedText = text; capturedContext = ctx; })
            .Returns(new FactGateResult(true, []));
        var sut = CreateSut();

        await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.NotNull(capturedContext);
        Assert.Contains("Kubernetes-Erfahrung macht diesen Kandidaten stark.", capturedText);
    }

    private static string TwoBulletJson() =>
        """
        {
          "global_score": 3.8,
          "dimensions": { "cv_match": 4.0, "role_alignment": 3.5, "culture": 3.0, "red_flags": 1.0 },
          "role_summary": "Junior .NET Backend, Remote",
          "culture_screen": "caution",
          "warnings": [],
          "bullet_rewrites": [
            {
              "original": "Entwickelte ASP.NET Core REST APIs für interne Tools.",
              "rewritten": "ASP.NET Core REST APIs für interne Tools entwickelt.",
              "reasoning": "Nutzt die Wörter der Anzeige.",
              "evidence_line": "Entwickelte ASP.NET Core REST APIs für interne Tools."
            },
            {
              "original": "Entwickelte ASP.NET Core REST APIs für interne Tools.",
              "rewritten": "Kubernetes-Cluster in Produktion betrieben.",
              "reasoning": "Passt zur Anzeige.",
              "evidence_line": "Betrieb von Kubernetes-Clustern."
            }
          ]
        }
        """;

    [Fact]
    public async Task Analyze_OneBulletFailsItsOwnCheck_KeepsTheOtherAndFlagsTheIndex()
    {
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Returns(new FactGateResult(true, []));
        _gate.Setup(g => g.VerifyBullet(
                It.Is<BulletRewriteSuggestion>(b => b.RewrittenBullet.Contains("Kubernetes")),
                It.IsAny<string?>(),
                It.IsAny<FactGateContext>()))
            .Returns(new FactGateBulletResult(false, [
                new FactGateViolation(FactGateViolationTypes.InventedSkill, "Kubernetes", "not in CV")
            ]));
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse(TwoBulletJson(), "llama-3.3-70b-versatile", 100, 80));
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        // Both rewrites survive; only the bad one is marked, so the good one is not collateral damage.
        Assert.Equal(2, report.Bullets.Count);
        Assert.Equal([1], report.UnverifiedBulletIndices);
        Assert.Contains(report.FactViolations, v => v.Snippet == "Kubernetes");
        Assert.NotEqual("", report.RoleSummary);
    }

    [Fact]
    public async Task Analyze_EveryBulletPasses_LeavesNoIndicesFlagged()
    {
        SetupHappyCollaborators(ValidV2Json());
        var sut = CreateSut();

        var report = await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.True(report.UnverifiedBulletIndices is null or []);
        Assert.Empty(report.FactViolations);
    }

    [Fact]
    public async Task Analyze_BulletEvidenceLine_IsPassedToTheBulletCheck()
    {
        string? capturedEvidence = null;
        SetupHappyCollaborators(ValidV2Json());
        _gate.Setup(g => g.VerifyBullet(
                It.IsAny<BulletRewriteSuggestion>(),
                It.IsAny<string?>(),
                It.IsAny<FactGateContext>()))
            .Callback<BulletRewriteSuggestion, string?, FactGateContext>((_, evidence, _) => capturedEvidence = evidence)
            .Returns(new FactGateBulletResult(true, []));
        var sut = CreateSut();

        await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("Entwickelte ASP.NET Core REST APIs für interne Tools.", capturedEvidence);
    }

    [Fact]
    public async Task Analyze_NarrativeCheck_ExcludesBulletTextButKeepsRoleSummaryAndWarnings()
    {
        string? narrative = null;
        _gap.Setup(g => g.Classify(It.IsAny<string>(), It.IsAny<string>())).Returns(OkGap());
        _gate.Setup(g => g.Verify(It.IsAny<string>(), It.IsAny<FactGateContext>()))
            .Callback<string, FactGateContext>((text, _) => narrative = text)
            .Returns(new FactGateResult(true, []));
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse(
                TwoBulletJson().Replace("\"warnings\": []", "\"warnings\": [\"Englisch B2 statt C1.\"]"),
                "llama-3.3-70b-versatile",
                100,
                80));
        var sut = CreateSut();

        await sut.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.NotNull(narrative);
        Assert.Contains("Junior .NET Backend, Remote", narrative);
        Assert.Contains("Englisch B2 statt C1.", narrative);
        Assert.DoesNotContain("Kubernetes", narrative);
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
