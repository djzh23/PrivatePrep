using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PrivatePrep.Controllers;
using PrivatePrep.Models;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Profile;
using PrivatePrep.Services.Reports;
using PrivatePrep.Services.SkillGap;
using PrivatePrep.Services.Tracking;

namespace PrivatePrep.Tests;

public class AgentControllerTests
{
    private readonly Mock<IAnalyzeService> _analyzeMock = new();
    private readonly Mock<ICareerProfileReader> _profileMock = new();
    private readonly Mock<ILogger<AgentController>> _loggerMock = new();
    private readonly Mock<UsageService> _usageMock = new();
    private readonly Mock<IAppUserContext> _userContextMock = new();
    private readonly Mock<TokenTrackingService> _tokenTrackingMock = new();
    private readonly Mock<IAnalysisReportService> _reportMock = new();

    public AgentControllerTests()
    {
        _tokenTrackingMock
            .Setup(t => t.TrackUsageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _usageMock.Setup(u => u.GetBackendInfo()).Returns(new UsageBackendInfo("postgres", "postgres", false, null));
        _tokenTrackingMock.Setup(t => t.GetBackendInfo()).Returns(new TokenTrackingBackendInfo("postgres", "postgres", false, null));
    }

    private AgentController CreateController()
    {
        var controller = new AgentController(
            _analyzeMock.Object,
            _profileMock.Object,
            _usageMock.Object,
            _userContextMock.Object,
            _tokenTrackingMock.Object,
            _reportMock.Object,
            _loggerMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static string LongJd() =>
        "Anforderungen\nC# und ASP.NET Core für das Backend-Team in Deutschland. "
        + "Die Stelle ist unbefristet und das Umfeld ist kollegial.";

    private static string SampleCv => "Kenntnisse\nC#\nBerufserfahrung\nAPIs gebaut.";

    private static AnalyzeRequestDto AnalyzeBody(string jd, string? cv = null)
    {
        var text = cv ?? SampleCv;
        return new AnalyzeRequestDto
        {
            JobDescription = jd,
            CvText = text,
            CvContentHash = CvContentHasher.Sha256Hex(text.Trim()),
        };
    }

    private void SetupStoredFingerprint(string? cv = null)
    {
        var text = (cv ?? SampleCv).Trim();
        var hash = CvContentHasher.Sha256Hex(text);
        _profileMock
            .Setup(p => p.GetCvFingerprintAsync("user_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CvFingerprint(hash, text.Length));
    }

    [Fact]
    public async Task Analyze_Anonymous_Returns401()
    {
        _userContextMock.Setup(u => u.UserId).Returns("ip:127.0.0.1");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(true);

        var result = await CreateController().Analyze(new AnalyzeRequestDto { JobDescription = LongJd() });

        var obj = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    [Fact]
    public async Task Analyze_ShortJd_Returns400()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);

        var result = await CreateController().Analyze(new AnalyzeRequestDto { JobDescription = "zu kurz" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    private static AnalysisReport SampleSavedReport(Guid? id = null, Guid? inboxJobId = null) => new(
        id ?? Guid.NewGuid(),
        "user_abc",
        inboxJobId,
        "{}",
        CvContentHasher.Sha256Hex(SampleCv.Trim()),
        CvContentHasher.Sha256Hex(LongJd().Trim()),
        SampleCv.Trim().Length,
        LongJd().Trim().Length,
        "groq-model",
        3.8m,
        DateTime.UtcNow,
        DateTime.UtcNow);

    [Fact]
    public async Task Analyze_ValidRequest_Returns200()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        SetupStoredFingerprint();
        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ReturnsAsync(new UsageCheckResult
            {
                Allowed = true,
                UsageToday = 1,
                DailyLimit = 3,
                Plan = "free"
            });
        _profileMock.Setup(p => p.GetProfile("user_abc"))
            .ReturnsAsync(new CareerProfile { UserId = "user_abc", Story = "Ich will Backend." });
        _analyzeMock.Setup(a => a.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeReport(
                3.8m,
                new ScoreDimensions(4.0m, 3.5m, 3.0m, 1.0m),
                new SkillGapReport(["C#"], [], [], ["C#"], "ok"),
                [],
                "Junior Backend",
                [],
                "not_evaluated",
                []));
        var saved = SampleSavedReport();
        _reportMock.Setup(r => r.UpsertAsync(
                "user_abc", null, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var result = await CreateController().Analyze(AnalyzeBody(LongJd()));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<AnalyzeResponse>(ok.Value);
        Assert.Equal(3.8m, report.GlobalScore);
        Assert.Equal(saved.Id, report.ReportId);
        _analyzeMock.Verify(
            a => a.AnalyzeAsync(
                It.Is<AnalyzeRequest>(r => r.CvText == SampleCv.Trim() && r.StoryText == "Ich will Backend."),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Analyze_WithInboxJobId_PassesItToReportService()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        SetupStoredFingerprint();
        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ReturnsAsync(new UsageCheckResult { Allowed = true, UsageToday = 1, DailyLimit = 3, Plan = "free" });
        _profileMock.Setup(p => p.GetProfile("user_abc"))
            .ReturnsAsync(new CareerProfile { UserId = "user_abc", Story = "" });
        _analyzeMock.Setup(a => a.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeReport(
                3.8m,
                new ScoreDimensions(4.0m, 3.5m, 3.0m, 1.0m),
                new SkillGapReport(["C#"], [], [], ["C#"], "ok"),
                [], "Junior Backend", [], "not_evaluated", []));
        var jobId = Guid.NewGuid();
        var saved = SampleSavedReport(inboxJobId: jobId);
        _reportMock.Setup(r => r.UpsertAsync(
                "user_abc", jobId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var body = AnalyzeBody(LongJd());
        body.InboxJobId = jobId;
        var result = await CreateController().Analyze(body);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<AnalyzeResponse>(ok.Value);
        Assert.Equal(saved.Id, report.ReportId);
        _reportMock.Verify(
            r => r.UpsertAsync(
                "user_abc", jobId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Analyze_InboxJobNotFound_Returns404()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        SetupStoredFingerprint();
        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ReturnsAsync(new UsageCheckResult { Allowed = true, UsageToday = 1, DailyLimit = 3, Plan = "free" });
        _profileMock.Setup(p => p.GetProfile("user_abc"))
            .ReturnsAsync(new CareerProfile { UserId = "user_abc", Story = "" });
        _analyzeMock.Setup(a => a.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeReport(
                3.8m,
                new ScoreDimensions(4.0m, 3.5m, 3.0m, 1.0m),
                new SkillGapReport(["C#"], [], [], ["C#"], "ok"),
                [], "Junior Backend", [], "not_evaluated", []));
        _reportMock.Setup(r => r.UpsertAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var body = AnalyzeBody(LongJd());
        body.InboxJobId = Guid.NewGuid();
        var result = await CreateController().Analyze(body);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task Analyze_HashMismatch_Returns400()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);

        var result = await CreateController().Analyze(new AnalyzeRequestDto
        {
            JobDescription = LongJd(),
            CvText = SampleCv,
            CvContentHash = CvContentHasher.Sha256Hex("other-cv"),
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, bad.StatusCode);
        _usageMock.Verify(u => u.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Analyze_StaleCache_Returns400()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        _profileMock
            .Setup(p => p.GetCvFingerprintAsync("user_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CvFingerprint(CvContentHasher.Sha256Hex("older-upload"), 12));

        var result = await CreateController().Analyze(AnalyzeBody(LongJd()));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, bad.StatusCode);
        _usageMock.Verify(u => u.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Analyze_UsageLimitReached_Returns429()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        SetupStoredFingerprint();
        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ReturnsAsync(new UsageCheckResult
            {
                Allowed = false,
                Reason = "daily_limit",
                Message = "Tageslimit erreicht.",
                UsageToday = 3,
                DailyLimit = 3,
                Plan = "free"
            });

        var result = await CreateController().Analyze(AnalyzeBody(LongJd()));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(429, obj.StatusCode);
    }

    [Fact]
    public async Task Analyze_UsageServiceThrows_Returns503()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        SetupStoredFingerprint();
        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ThrowsAsync(new InvalidOperationException("Postgres connection refused"));

        var result = await CreateController().Analyze(AnalyzeBody(LongJd()));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, obj.StatusCode);
    }

    [Fact]
    public async Task Analyze_MissingCv_Returns400()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);

        var result = await CreateController().Analyze(new AnalyzeRequestDto { JobDescription = LongJd() });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, bad.StatusCode);
        _usageMock.Verify(u => u.CheckAndIncrementAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
