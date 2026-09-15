using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PrivatePrep.Controllers;
using PrivatePrep.Models;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Profile;
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

    [Fact]
    public async Task Analyze_Anonymous_Returns401()
    {
        _userContextMock.Setup(u => u.UserId).Returns("ip:127.0.0.1");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(true);

        var result = await CreateController().Analyze(new AnalyzeRequestDto(LongJd()));

        var obj = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    [Fact]
    public async Task Analyze_ShortJd_Returns400()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);

        var result = await CreateController().Analyze(new AnalyzeRequestDto("zu kurz"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Analyze_ValidRequest_Returns200()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
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
        _profileMock.Setup(p => p.GetCvRawTextAsync("user_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Kenntnisse\nC#\nBerufserfahrung\nAPIs gebaut.");
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

        var result = await CreateController().Analyze(new AnalyzeRequestDto(LongJd()));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<AnalyzeReport>(ok.Value);
        Assert.Equal(3.8m, report.GlobalScore);
    }

    [Fact]
    public async Task Analyze_UsageLimitReached_Returns429()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
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

        var result = await CreateController().Analyze(new AnalyzeRequestDto(LongJd()));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(429, obj.StatusCode);
    }

    [Fact]
    public async Task Analyze_UsageServiceThrows_Returns503()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ThrowsAsync(new InvalidOperationException("Postgres connection refused"));

        var result = await CreateController().Analyze(new AnalyzeRequestDto(LongJd()));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, obj.StatusCode);
    }

    [Fact]
    public async Task Analyze_IncompleteProfile_Returns400()
    {
        _userContextMock.Setup(u => u.UserId).Returns("user_abc");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ReturnsAsync(new UsageCheckResult { Allowed = true, UsageToday = 1, DailyLimit = 3, Plan = "free" });
        _profileMock.Setup(p => p.GetProfile("user_abc")).ReturnsAsync((CareerProfile?)null);
        _profileMock.Setup(p => p.GetCvRawTextAsync("user_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _analyzeMock.Setup(a => a.AnalyzeAsync(It.IsAny<AnalyzeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AnalyzeException("profile_incomplete", "Bitte Profil vervollständigen"));

        var result = await CreateController().Analyze(new AnalyzeRequestDto(LongJd()));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, bad.StatusCode);
    }
}
