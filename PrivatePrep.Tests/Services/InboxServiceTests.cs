using Microsoft.EntityFrameworkCore;
using PrivatePrep.Data;
using PrivatePrep.Data.Entities;
using PrivatePrep.Models;
using PrivatePrep.Services.Inbox;

namespace PrivatePrep.Tests.Services;

public sealed class InboxServiceTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");

    private PrivatePrepDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<PrivatePrepDbContext>().UseInMemoryDatabase(_dbName).Options);

    private InboxService CreateSut() => new(OpenDb());

    public void Dispose()
    {
        using var db = OpenDb();
        db.Database.EnsureDeleted();
    }

    private static CreateInboxJobRequest Request(
        string title = "Backend Entwickler",
        string company = "Acme GmbH",
        string? location = "Berlin",
        string sourceUrl = "https://example.com/job/1",
        InboxSourceKind kind = InboxSourceKind.LinkedIn,
        string? rawText = null) =>
        new(title, company, location, sourceUrl, kind, rawText ?? new string('x', 150));

    private async Task SetStatusAsync(Guid id, InboxJobStatus status)
    {
        await using var db = OpenDb();
        var entity = await db.InboxJobs.SingleAsync(x => x.Id == id);
        entity.Status = status;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateOrUpdate_NewUrl_CreatesNewEntry()
    {
        var sut = CreateSut();

        var job = await sut.CreateOrUpdateAsync("user_1", Request(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal("Backend Entwickler", job.Title);
        Assert.Equal(InboxJobStatus.New, job.Status);
        var all = await sut.ListForUserAsync("user_1", null, 50, CancellationToken.None);
        Assert.Single(all);
    }

    [Fact]
    public async Task CreateOrUpdate_SameUserAndUrl_UpdatesExistingEntryInsteadOfDuplicating()
    {
        var sut = CreateSut();
        var first = await sut.CreateOrUpdateAsync("user_1", Request(rawText: new string('a', 150)), CancellationToken.None);

        var second = await sut.CreateOrUpdateAsync(
            "user_1",
            Request(title: "Senior Backend Entwickler", rawText: new string('b', 150)),
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Senior Backend Entwickler", second.Title);
        var all = await sut.ListForUserAsync("user_1", null, 50, CancellationToken.None);
        Assert.Single(all);
    }

    [Fact]
    public async Task CreateOrUpdate_OnUpdatePath_LeavesExtractedAtAndStatusUntouched()
    {
        // Two separate service instances, each its own DbContext, mirror two separate HTTP
        // requests (a real scoped DbContext never outlives one request).
        var first = await CreateSut().CreateOrUpdateAsync("user_1", Request(), CancellationToken.None);
        await SetStatusAsync(first.Id, InboxJobStatus.Analyzed);

        var second = await CreateSut().CreateOrUpdateAsync("user_1", Request(title: "Aktualisiert"), CancellationToken.None);

        Assert.Equal(first.ExtractedAt, second.ExtractedAt);
        Assert.Equal(InboxJobStatus.Analyzed, second.Status);
    }

    [Fact]
    public async Task List_OnlyReturnsJobsForTheRequestingUser()
    {
        var sut = CreateSut();
        await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/a"), CancellationToken.None);
        await sut.CreateOrUpdateAsync("user_2", Request(sourceUrl: "https://example.com/b"), CancellationToken.None);

        var jobs = await sut.ListForUserAsync("user_1", null, 50, CancellationToken.None);

        var only = Assert.Single(jobs);
        Assert.Equal("user_1", only.UserId);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var sut = CreateSut();
        var kept = await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/new"), CancellationToken.None);
        var toArchive = await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/archived"), CancellationToken.None);
        await SetStatusAsync(toArchive.Id, InboxJobStatus.Archived);

        var newOnly = await sut.ListForUserAsync("user_1", InboxJobStatus.New, 50, CancellationToken.None);

        var only = Assert.Single(newOnly);
        Assert.Equal(kept.Id, only.Id);
    }

    [Fact]
    public async Task List_IsLimitedToAMaximumOfOneHundredEvenIfMoreAreRequested()
    {
        var sut = CreateSut();
        for (var i = 0; i < 120; i++)
            await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: $"https://example.com/{i}"), CancellationToken.None);

        var jobs = await sut.ListForUserAsync("user_1", null, 500, CancellationToken.None);

        Assert.Equal(100, jobs.Count);
    }

    [Fact]
    public async Task GetByIdForUser_JobBelongsToAnotherUser_ReturnsNullNotAnError()
    {
        var sut = CreateSut();
        var job = await sut.CreateOrUpdateAsync("user_1", Request(), CancellationToken.None);

        var result = await sut.GetByIdForUserAsync("user_2", job.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Update_OnlyChangesTitleCompanyAndRawText()
    {
        var sut = CreateSut();
        var job = await sut.CreateOrUpdateAsync("user_1", Request(location: "Hamburg"), CancellationToken.None);

        var updated = await sut.UpdateAsync(
            "user_1",
            job.Id,
            new UpdateInboxJobRequest("Neuer Titel", null, null),
            CancellationToken.None);

        Assert.Equal("Neuer Titel", updated.Title);
        Assert.Equal(job.Company, updated.Company);
        Assert.Equal(job.Location, updated.Location);
        Assert.Equal(job.Status, updated.Status);
        Assert.Equal(job.ExtractedAt, updated.ExtractedAt);
        Assert.Equal(job.SourceKind, updated.SourceKind);
    }

    [Fact]
    public async Task Update_JobBelongsToAnotherUser_ThrowsKeyNotFound()
    {
        var sut = CreateSut();
        var job = await sut.CreateOrUpdateAsync("user_1", Request(), CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.UpdateAsync("user_2", job.Id, new UpdateInboxJobRequest("x", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheEntry()
    {
        var sut = CreateSut();
        var job = await sut.CreateOrUpdateAsync("user_1", Request(), CancellationToken.None);

        var deleted = await sut.DeleteAsync("user_1", job.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await sut.GetByIdForUserAsync("user_1", job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_JobBelongsToAnotherUser_ReturnsFalseAndLeavesItInPlace()
    {
        var sut = CreateSut();
        var job = await sut.CreateOrUpdateAsync("user_1", Request(), CancellationToken.None);

        var deleted = await sut.DeleteAsync("user_2", job.Id, CancellationToken.None);

        Assert.False(deleted);
        Assert.NotNull(await sut.GetByIdForUserAsync("user_1", job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_TruncatesRawTextToAPreview()
    {
        var sut = CreateSut();
        var longText = new string('x', 400);
        await sut.CreateOrUpdateAsync("user_1", Request(rawText: longText), CancellationToken.None);

        var listed = Assert.Single(await sut.ListForUserAsync("user_1", null, 50, CancellationToken.None));
        var full = await sut.GetByIdForUserAsync("user_1", listed.Id, CancellationToken.None);

        Assert.Equal(InboxService.RawTextPreviewLength, listed.RawText.Length);
        Assert.Equal(longText[..InboxService.RawTextPreviewLength], listed.RawText);
        Assert.Equal(400, full!.RawText.Length);
    }

    [Fact]
    public async Task CountForUser_OnlyCountsTheRequestedStatus()
    {
        var sut = CreateSut();
        var analyzed = await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/a"), CancellationToken.None);
        await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/b"), CancellationToken.None);
        await SetStatusAsync(analyzed.Id, InboxJobStatus.Analyzed);

        Assert.Equal(1, await sut.CountForUserAsync("user_1", InboxJobStatus.New, CancellationToken.None));
        Assert.Equal(1, await sut.CountForUserAsync("user_1", InboxJobStatus.Analyzed, CancellationToken.None));
        Assert.Equal(0, await sut.CountForUserAsync("user_2", InboxJobStatus.New, CancellationToken.None));
    }

    [Fact]
    public async Task CountActive_OnlyCountsNewStatus()
    {
        var sut = CreateSut();
        var a = await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/a"), CancellationToken.None);
        await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/b"), CancellationToken.None);
        await SetStatusAsync(a.Id, InboxJobStatus.Analyzed);

        var count = await sut.CountActiveForUserAsync("user_1", CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountRecent_OnlyCountsJobsInsideTheWindow()
    {
        var sut = CreateSut();
        var recent = await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/recent"), CancellationToken.None);
        var old = await sut.CreateOrUpdateAsync("user_1", Request(sourceUrl: "https://example.com/old"), CancellationToken.None);
        await using (var db = OpenDb())
        {
            var entity = await db.InboxJobs.SingleAsync(x => x.Id == old.Id);
            entity.CreatedAt = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        }

        var count = await sut.CountRecentForUserAsync("user_1", TimeSpan.FromMinutes(60), CancellationToken.None);

        Assert.Equal(1, count);
        _ = recent;
    }
}
