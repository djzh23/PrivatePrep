using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PrivatePrep.Controllers;
using PrivatePrep.Models;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Tracking;

namespace PrivatePrep.Tests;

public class AgentControllerTests
{
    private readonly Mock<IAgentService> _agentServiceMock = new();
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
            _agentServiceMock.Object,
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

    [Fact]
    public async Task Ask_EmptyMessage_Returns400()
    {
        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest(""));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Ask_MessageOver4000Chars_Returns400()
    {
        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest(new string('x', 4001)));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Ask_ValidMessage_Returns200WithAgentResponse()
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

        _agentServiceMock.Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
            .ReturnsAsync(new AgentResponse("Test reply", "jobanalyzer"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("What is the weather?", ToolType: "jobanalyzer"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentResponse>(ok.Value);
        Assert.Equal("Test reply", response.Reply);
        Assert.Equal("jobanalyzer", response.ToolUsed);
    }

    [Fact]
    public async Task Ask_AnonymousUser_Returns200WithAgentResponse()
    {
        _userContextMock.Setup(u => u.UserId).Returns("ip:127.0.0.1");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(true);

        _usageMock.Setup(u => u.CheckAndIncrementAsync("ip:127.0.0.1", true))
            .ReturnsAsync(new UsageCheckResult
            {
                Allowed = true,
                UsageToday = 1,
                DailyLimit = 2,
                Plan = "anonymous"
            });

        _agentServiceMock.Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
            .ReturnsAsync(new AgentResponse("Berlin weather is sunny.", "jobanalyzer"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Wie ist das Wetter in Berlin?", ToolType: "jobanalyzer"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentResponse>(ok.Value);
        Assert.Equal("Berlin weather is sunny.", response.Reply);
    }

    [Fact]
    public async Task Ask_AnonymousUsageLimitReached_Returns429()
    {
        _userContextMock.Setup(u => u.UserId).Returns("ip:127.0.0.1");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(true);

        _usageMock.Setup(u => u.CheckAndIncrementAsync("ip:127.0.0.1", true))
            .ReturnsAsync(new UsageCheckResult
            {
                Allowed = false,
                Reason = "anonymous_limit",
                Message = "Melde dich an, um 3 kostenlose Analysen pro Tag zu erhalten.",
                UsageToday = 2,
                DailyLimit = 2,
                Plan = "anonymous"
            });

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Third message", ToolType: "jobanalyzer"));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(429, obj.StatusCode);
    }

    [Fact]
    public async Task Ask_UsageServiceThrows_Returns503()
    {
        _userContextMock.Setup(u => u.UserId).Returns("ip:127.0.0.1");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(true);

        _usageMock.Setup(u => u.CheckAndIncrementAsync("ip:127.0.0.1", true))
            .ThrowsAsync(new InvalidOperationException("Postgres connection refused"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Hello", ToolType: "jobanalyzer"));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, obj.StatusCode);
    }

    [Fact]
    public void Health_ReturnsOkWithStatusAndTimestamp()
    {
        var controller = CreateController();
        var result = controller.Health();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("ok", payloadJson);
        Assert.Contains("timestamp", payloadJson);
    }
}
