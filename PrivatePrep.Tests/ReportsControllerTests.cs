using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PrivatePrep.Controllers;
using PrivatePrep.Models;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Inbox;
using PrivatePrep.Services.Reports;

namespace PrivatePrep.Tests;

/// <summary>Controller-level tests mirroring InboxControllerTests's style (direct instantiation, mocked services).</summary>
public class ReportsControllerTests
{
    private readonly Mock<IAnalysisReportService> _reportMock = new();
    private readonly Mock<IInboxService> _inboxMock = new();
    private readonly Mock<IAppUserContext> _userContextMock = new();
    private readonly Mock<ILogger<ReportsController>> _loggerMock = new();

    private ReportsController CreateController()
    {
        var controller = new ReportsController(_reportMock.Object, _inboxMock.Object, _userContextMock.Object, _loggerMock.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private void SignedInAs(string userId)
    {
        _userContextMock.Setup(u => u.UserId).Returns(userId);
        _userContextMock.Setup(u => u.IsAnonymous).Returns(false);
    }

    private void Anonymous()
    {
        _userContextMock.Setup(u => u.UserId).Returns("ip:127.0.0.1");
        _userContextMock.Setup(u => u.IsAnonymous).Returns(true);
    }

    private static AnalysisReport SampleReport(string userId = "user_abc", Guid? inboxJobId = null) => new(
        Guid.NewGuid(),
        userId,
        inboxJobId,
        "{\"globalScore\":3.8}",
        new string('a', 64),
        new string('b', 64),
        1000,
        500,
        "groq-model",
        3.8m,
        DateTime.UtcNow,
        DateTime.UtcNow);

    private static InboxJob SampleJob(Guid id, string userId = "user_abc") => new(
        id, userId, "Backend Entwickler", "Acme GmbH", "Berlin", "https://example.com/job/1",
        InboxSourceKind.LinkedIn, new string('x', 150), InboxJobStatus.Analyzed,
        DateTime.UtcNow, DateTime.UtcNow, null, DateTime.UtcNow, DateTime.UtcNow);

    // ---- GET /api/reports/{id} ---------------------------------------------------------------

    [Fact]
    public async Task GetById_OwnReport_Returns200()
    {
        SignedInAs("user_abc");
        var report = SampleReport();
        _reportMock.Setup(s => s.GetByIdForUserAsync("user_abc", report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);

        var result = await CreateController().GetById(report.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AnalysisReportResponse>(ok.Value);
        Assert.Equal(report.Id, body.Id);
        Assert.Equal(report.ReportJson, body.ReportJson);
    }

    [Fact]
    public async Task GetById_ForeignOrMissingReport_Returns404()
    {
        SignedInAs("user_abc");
        var id = Guid.NewGuid();
        _reportMock.Setup(s => s.GetByIdForUserAsync("user_abc", id, It.IsAny<CancellationToken>())).ReturnsAsync((AnalysisReport?)null);

        var result = await CreateController().GetById(id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetById_NotSignedIn_Returns401()
    {
        Anonymous();

        var result = await CreateController().GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        _reportMock.Verify(s => s.GetByIdForUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- GET /api/reports -------------------------------------------------------------------

    [Fact]
    public async Task List_ReportWithoutInboxJob_DoesNotCallInboxService()
    {
        SignedInAs("user_abc");
        _reportMock.Setup(s => s.ListForUserAsync("user_abc", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleReport()]);

        var result = await CreateController().List(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<List<AnalysisReportListItemResponse>>(ok.Value);
        var only = Assert.Single(items);
        Assert.Null(only.InboxJobTitle);
        _inboxMock.Verify(s => s.GetByIdForUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_ReportWithInboxJob_IncludesTitleAndCompany()
    {
        SignedInAs("user_abc");
        var jobId = Guid.NewGuid();
        _reportMock.Setup(s => s.ListForUserAsync("user_abc", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleReport(inboxJobId: jobId)]);
        _inboxMock.Setup(s => s.GetByIdForUserAsync("user_abc", jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleJob(jobId));

        var result = await CreateController().List(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<List<AnalysisReportListItemResponse>>(ok.Value);
        var only = Assert.Single(items);
        Assert.Equal("Backend Entwickler", only.InboxJobTitle);
        Assert.Equal("Acme GmbH", only.InboxJobCompany);
    }

    // ---- DELETE /api/reports/{id} -------------------------------------------------------------

    [Fact]
    public async Task Delete_OwnReport_Returns204()
    {
        SignedInAs("user_abc");
        var id = Guid.NewGuid();
        _reportMock.Setup(s => s.DeleteAsync("user_abc", id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateController().Delete(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ForeignOrMissingReport_Returns404()
    {
        SignedInAs("user_abc");
        var id = Guid.NewGuid();
        _reportMock.Setup(s => s.DeleteAsync("user_abc", id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateController().Delete(id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_NotSignedIn_Returns401()
    {
        Anonymous();

        var result = await CreateController().Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        _reportMock.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
