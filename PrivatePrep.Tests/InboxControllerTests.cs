using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PrivatePrep.Controllers;
using PrivatePrep.Models;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Inbox;

namespace PrivatePrep.Tests;

/// <summary>
/// Controller-level tests mirroring AgentControllerTests's style (direct instantiation, mocked
/// services). No WebApplicationFactory/HTTP-integration pattern exists yet in this test project.
/// </summary>
public class InboxControllerTests
{
    private readonly Mock<IInboxService> _inboxMock = new();
    private readonly Mock<IAppUserContext> _userContextMock = new();
    private readonly Mock<ILogger<InboxController>> _loggerMock = new();

    private InboxController CreateController()
    {
        var controller = new InboxController(_inboxMock.Object, _userContextMock.Object, _loggerMock.Object);
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

    private static CreateInboxJobRequest ValidCreateRequest() => new(
        "Backend Entwickler",
        "Acme GmbH",
        "Berlin",
        "https://example.com/job/1",
        InboxSourceKind.LinkedIn,
        new string('x', 150));

    private static InboxJob SampleJob(string userId = "user_abc") => new(
        Guid.NewGuid(),
        userId,
        "Backend Entwickler",
        "Acme GmbH",
        "Berlin",
        "https://example.com/job/1",
        InboxSourceKind.LinkedIn,
        new string('x', 150),
        InboxJobStatus.New,
        DateTime.UtcNow,
        null,
        null,
        DateTime.UtcNow,
        DateTime.UtcNow);

    // ---- POST /api/inbox ----------------------------------------------------------------

    [Fact]
    public async Task Create_ValidBody_Returns201WithLocationHeader()
    {
        SignedInAs("user_abc");
        _inboxMock.Setup(s => s.CountActiveForUserAsync("user_abc", It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _inboxMock.Setup(s => s.CountRecentForUserAsync("user_abc", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var job = SampleJob();
        _inboxMock.Setup(s => s.CreateOrUpdateAsync("user_abc", It.IsAny<CreateInboxJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await CreateController().Create(ValidCreateRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(InboxController.GetById), created.ActionName);
        var body = Assert.IsType<InboxJobResponse>(created.Value);
        Assert.Equal(job.Id, body.Id);
    }

    [Fact]
    public async Task Create_NotSignedIn_Returns401()
    {
        Anonymous();

        var result = await CreateController().Create(ValidCreateRequest(), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        _inboxMock.Verify(s => s.CreateOrUpdateAsync(It.IsAny<string>(), It.IsAny<CreateInboxJobRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_InboxAtCapacity_Returns429()
    {
        SignedInAs("user_abc");
        _inboxMock.Setup(s => s.CountActiveForUserAsync("user_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxController.MaxActiveJobs);

        var result = await CreateController().Create(ValidCreateRequest(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, obj.StatusCode);
        _inboxMock.Verify(s => s.CreateOrUpdateAsync(It.IsAny<string>(), It.IsAny<CreateInboxJobRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_TooManyRecentSaves_Returns429()
    {
        SignedInAs("user_abc");
        _inboxMock.Setup(s => s.CountActiveForUserAsync("user_abc", It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _inboxMock.Setup(s => s.CountRecentForUserAsync("user_abc", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxController.MaxNewJobsPerWindow);

        var result = await CreateController().Create(ValidCreateRequest(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, obj.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyTitle_Returns400()
    {
        SignedInAs("user_abc");
        var request = ValidCreateRequest() with { Title = "  " };

        var result = await CreateController().Create(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, bad.StatusCode);
        _inboxMock.Verify(s => s.CreateOrUpdateAsync(It.IsAny<string>(), It.IsAny<CreateInboxJobRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_RawTextTooShort_Returns400()
    {
        SignedInAs("user_abc");
        var request = ValidCreateRequest() with { RawText = "zu kurz" };

        var result = await CreateController().Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_InvalidSourceUrl_Returns400()
    {
        SignedInAs("user_abc");
        var request = ValidCreateRequest() with { SourceUrl = "not a url" };

        var result = await CreateController().Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ---- GET /api/inbox -------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsJobsFromTheService()
    {
        SignedInAs("user_abc");
        _inboxMock.Setup(s => s.ListForUserAsync("user_abc", null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleJob()]);

        var result = await CreateController().List(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<List<InboxJobListItemResponse>>(ok.Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task List_InvalidStatus_Returns400()
    {
        SignedInAs("user_abc");

        var result = await CreateController().List("not_a_status", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ---- GET /api/inbox/{id} ---------------------------------------------------------------

    [Fact]
    public async Task GetById_OwnJob_Returns200()
    {
        SignedInAs("user_abc");
        var job = SampleJob();
        _inboxMock.Setup(s => s.GetByIdForUserAsync("user_abc", job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var result = await CreateController().GetById(job.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<InboxJobResponse>(ok.Value);
        Assert.Equal(job.RawText, body.RawText);
    }

    [Fact]
    public async Task GetById_ForeignJob_Returns404()
    {
        SignedInAs("user_abc");
        var id = Guid.NewGuid();
        _inboxMock.Setup(s => s.GetByIdForUserAsync("user_abc", id, It.IsAny<CancellationToken>())).ReturnsAsync((InboxJob?)null);

        var result = await CreateController().GetById(id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- PATCH /api/inbox/{id} -------------------------------------------------------------

    [Fact]
    public async Task Update_ValidBody_Returns200()
    {
        SignedInAs("user_abc");
        var job = SampleJob() with { Title = "Neuer Titel" };
        _inboxMock.Setup(s => s.UpdateAsync("user_abc", job.Id, It.IsAny<UpdateInboxJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await CreateController().Update(job.Id, new UpdateInboxJobRequest("Neuer Titel", null, null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<InboxJobResponse>(ok.Value);
        Assert.Equal("Neuer Titel", body.Title);
    }

    [Fact]
    public async Task Update_ForeignJob_Returns404()
    {
        SignedInAs("user_abc");
        var id = Guid.NewGuid();
        _inboxMock.Setup(s => s.UpdateAsync("user_abc", id, It.IsAny<UpdateInboxJobRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await CreateController().Update(id, new UpdateInboxJobRequest("x", null, null), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---- DELETE /api/inbox/{id} -------------------------------------------------------------

    [Fact]
    public async Task Delete_OwnJob_Returns204()
    {
        SignedInAs("user_abc");
        var id = Guid.NewGuid();
        _inboxMock.Setup(s => s.DeleteAsync("user_abc", id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateController().Delete(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ForeignJob_Returns404()
    {
        SignedInAs("user_abc");
        var id = Guid.NewGuid();
        _inboxMock.Setup(s => s.DeleteAsync("user_abc", id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateController().Delete(id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
