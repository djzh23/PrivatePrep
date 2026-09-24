using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PrivatePrep.Data;
using PrivatePrep.Data.Entities;
using PrivatePrep.Models;
using PrivatePrep.Services.Reports;

namespace PrivatePrep.Tests.Services;

public sealed class AnalysisReportServiceTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");

    // AnalysisReportService wraps its cross-table writes in a real transaction (correct against
    // Postgres in production); the InMemory provider does not support transactions and by default
    // throws rather than silently no-opping. Ignoring TransactionIgnoredWarning here makes it the
    // documented no-op instead - this only relaxes the test double, not AnalysisReportService itself.
    private PrivatePrepDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<PrivatePrepDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    // A fresh AnalysisReportService per call, each with its own DbContext, mirrors two separate
    // HTTP requests (a real scoped DbContext never outlives one request) — same reasoning as
    // InboxServiceTests's CreateSut().
    private AnalysisReportService CreateSut() => new(OpenDb());

    public void Dispose()
    {
        using var db = OpenDb();
        db.Database.EnsureDeleted();
    }

    private async Task<Guid> SeedInboxJobAsync(string userId, InboxJobStatus status = InboxJobStatus.New)
    {
        await using var db = OpenDb();
        var job = new InboxJobEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Backend Entwickler",
            Company = "Acme GmbH",
            SourceUrl = $"https://example.com/{Guid.NewGuid()}",
            SourceKind = InboxSourceKind.LinkedIn,
            RawText = new string('x', 150),
            Status = status,
            ExtractedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.InboxJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<InboxJobEntity> LoadJobAsync(Guid id)
    {
        await using var db = OpenDb();
        return await db.InboxJobs.SingleAsync(x => x.Id == id);
    }

    // ---- UpsertAsync ------------------------------------------------------------------------

    [Fact]
    public async Task UpsertAsync_WithInboxJobId_CreatesNewReport_OnFirstCall()
    {
        var jobId = await SeedInboxJobAsync("user_1");

        var report = await CreateSut().UpsertAsync(
            "user_1", jobId, "{\"globalScore\":3.8}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.8m, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal(jobId, report.InboxJobId);
        Assert.Equal(3.8m, report.MatchScore);
    }

    [Fact]
    public async Task UpsertAsync_SameInboxJobId_UpdatesExistingReport_NoDuplicate()
    {
        var jobId = await SeedInboxJobAsync("user_1");
        var first = await CreateSut().UpsertAsync(
            "user_1", jobId, "{\"globalScore\":3.0}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.0m, CancellationToken.None);

        var second = await CreateSut().UpsertAsync(
            "user_1", jobId, "{\"globalScore\":4.2}", new string('c', 64), new string('d', 64),
            1200, 600, "groq-model", 4.2m, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(4.2m, second.MatchScore);
        await using var db = OpenDb();
        Assert.Equal(1, await db.AnalysisReports.CountAsync(x => x.InboxJobId == jobId));
    }

    [Fact]
    public async Task UpsertAsync_WithoutInboxJobId_AlwaysCreatesNewReport()
    {
        var sut = CreateSut();

        var first = await sut.UpsertAsync(
            "user_1", null, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.0m, CancellationToken.None);
        var second = await sut.UpsertAsync(
            "user_1", null, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.0m, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        var all = await sut.ListForUserAsync("user_1", 50, CancellationToken.None);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task UpsertAsync_WithInboxJobId_SetsJobStatusAnalyzedAtAndReportId()
    {
        var jobId = await SeedInboxJobAsync("user_1");

        var report = await CreateSut().UpsertAsync(
            "user_1", jobId, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.8m, CancellationToken.None);

        var job = await LoadJobAsync(jobId);
        Assert.Equal(InboxJobStatus.Analyzed, job.Status);
        Assert.NotNull(job.AnalyzedAt);
        Assert.Equal(report.Id, job.AnalysisReportId);
    }

    [Fact]
    public async Task UpsertAsync_InboxJobBelongsToAnotherUser_ThrowsKeyNotFound()
    {
        var jobId = await SeedInboxJobAsync("user_1");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateSut().UpsertAsync(
                "user_2", jobId, "{}", new string('a', 64), new string('b', 64),
                1000, 500, "groq-model", 3.8m, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAsync_InboxJobDoesNotExist_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateSut().UpsertAsync(
                "user_1", Guid.NewGuid(), "{}", new string('a', 64), new string('b', 64),
                1000, 500, "groq-model", 3.8m, CancellationToken.None));
    }

    // ---- GetByIdForUser / GetByInboxJobIdForUser --------------------------------------------

    [Fact]
    public async Task GetByIdForUser_ReportBelongsToAnotherUser_ReturnsNull()
    {
        var sut = CreateSut();
        var report = await sut.UpsertAsync(
            "user_1", null, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.0m, CancellationToken.None);

        var result = await sut.GetByIdForUserAsync("user_2", report.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByInboxJobIdForUser_FindsReportCorrectly()
    {
        var jobId = await SeedInboxJobAsync("user_1");
        var sut = CreateSut();
        var report = await sut.UpsertAsync(
            "user_1", jobId, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.8m, CancellationToken.None);

        var result = await sut.GetByInboxJobIdForUserAsync("user_1", jobId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(report.Id, result!.Id);
    }

    [Fact]
    public async Task GetByInboxJobIdForUser_NoReportYet_ReturnsNull()
    {
        var jobId = await SeedInboxJobAsync("user_1");

        var result = await CreateSut().GetByInboxJobIdForUserAsync("user_1", jobId, CancellationToken.None);

        Assert.Null(result);
    }

    // ---- ListForUserAsync ---------------------------------------------------------------------

    [Fact]
    public async Task ListForUserAsync_OnlyReturnsOwnReports()
    {
        var sut = CreateSut();
        await sut.UpsertAsync("user_1", null, "{}", new string('a', 64), new string('b', 64), 100, 50, "m", 3.0m, CancellationToken.None);
        await sut.UpsertAsync("user_2", null, "{}", new string('a', 64), new string('b', 64), 100, 50, "m", 3.0m, CancellationToken.None);

        var result = await sut.ListForUserAsync("user_1", 50, CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal("user_1", only.UserId);
    }

    // ---- DeleteAsync ----------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ReportWithInboxJob_ResetsJobToNewStatus()
    {
        var jobId = await SeedInboxJobAsync("user_1");
        var sut = CreateSut();
        var report = await sut.UpsertAsync(
            "user_1", jobId, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.8m, CancellationToken.None);

        var deleted = await sut.DeleteAsync("user_1", report.Id, CancellationToken.None);

        Assert.True(deleted);
        var job = await LoadJobAsync(jobId);
        Assert.Equal(InboxJobStatus.New, job.Status);
        Assert.Null(job.AnalyzedAt);
        Assert.Null(job.AnalysisReportId);
        Assert.Null(await sut.GetByIdForUserAsync("user_1", report.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_AdHocReportWithoutInboxJob_JustDeletesTheReport()
    {
        var sut = CreateSut();
        var report = await sut.UpsertAsync(
            "user_1", null, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.8m, CancellationToken.None);

        var deleted = await sut.DeleteAsync("user_1", report.Id, CancellationToken.None);

        Assert.True(deleted);
    }

    [Fact]
    public async Task DeleteAsync_ReportBelongsToAnotherUser_ReturnsFalseAndLeavesItInPlace()
    {
        var sut = CreateSut();
        var report = await sut.UpsertAsync(
            "user_1", null, "{}", new string('a', 64), new string('b', 64),
            1000, 500, "groq-model", 3.8m, CancellationToken.None);

        var deleted = await sut.DeleteAsync("user_2", report.Id, CancellationToken.None);

        Assert.False(deleted);
        Assert.NotNull(await sut.GetByIdForUserAsync("user_1", report.Id, CancellationToken.None));
    }
}
