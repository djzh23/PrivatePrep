using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PrivatePrep.Data;
using PrivatePrep.Data.Entities;
using PrivatePrep.Middleware;

namespace PrivatePrep.Tests.Middleware;

public sealed class UserResolutionMiddlewareTests : IDisposable
{
    private const string ClerkUserId = "user_2abc";
    private const string AnonymousKey = "ip:203.0.113.7";

    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private int _nextCalls;

    public void Dispose() => _cache.Dispose();

    // ---- anonymous callers ------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_PassesThroughWithoutProvisioning()
    {
        var (ctx, user) = await InvokeAsync((AnonymousKey, true, null));

        Assert.Equal(1, _nextCalls);
        Assert.True(user.IsAnonymous);
        Assert.Equal(AnonymousKey, user.UserId);
        Assert.False(ctx.User.Identity?.IsAuthenticated ?? false);
        await using var db = OpenDb();
        Assert.Empty(db.AppUsers);
    }

    [Fact]
    public async Task AuthenticatedWithBlankUserId_IsNotProvisioned()
    {
        var (_, _) = await InvokeAsync(("   ", false, PrincipalFor("   ")));

        Assert.Equal(1, _nextCalls);
        await using var db = OpenDb();
        Assert.Empty(db.AppUsers);
    }

    // ---- authenticated callers --------------------------------------------------------------

    [Fact]
    public async Task Authenticated_ExposesUserAndHandsPrincipalToHttpContext()
    {
        var principal = PrincipalFor(ClerkUserId, email: "anna@example.de");

        var (ctx, user) = await InvokeAsync((ClerkUserId, false, principal));

        Assert.Equal(1, _nextCalls);
        Assert.False(user.IsAnonymous);
        Assert.Equal(ClerkUserId, user.UserId);
        Assert.Same(principal, ctx.User);
        Assert.Equal("anna@example.de", ctx.User.FindFirst("email")?.Value);
    }

    [Fact]
    public async Task NewUser_IsProvisionedOnFirstRequest()
    {
        await InvokeAsync((ClerkUserId, false, PrincipalFor(ClerkUserId)));

        await using var db = OpenDb();
        var row = await db.AppUsers.SingleAsync();
        Assert.Equal(ClerkUserId, row.ClerkUserId);
        Assert.Equal("free", row.Plan);
        Assert.Equal("pending", row.OnboardingState);
    }

    [Fact]
    public async Task NewUser_HasAllTimestampsSet()
    {
        await InvokeAsync((ClerkUserId, false, PrincipalFor(ClerkUserId)));

        await using var db = OpenDb();
        var row = await db.AppUsers.SingleAsync();
        AssertRecent(row.CreatedAt);
        AssertRecent(row.UpdatedAt);
        AssertRecent(row.LastActiveAt);
    }

    [Fact]
    public async Task ExistingUser_IsLeftUntouched()
    {
        var created = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(new AppUserEntity
        {
            ClerkUserId = ClerkUserId,
            Plan = "premium",
            CreatedAt = created,
            UpdatedAt = created,
            LastActiveAt = created,
        });

        await InvokeAsync((ClerkUserId, false, PrincipalFor(ClerkUserId)));

        Assert.Equal(1, _nextCalls);
        await using var db = OpenDb();
        var row = await db.AppUsers.SingleAsync();
        Assert.Equal("premium", row.Plan);
        Assert.Equal(created, row.CreatedAt);
        Assert.Equal(created, row.UpdatedAt);
    }

    [Fact]
    public async Task ConcurrentInsertRace_RequestStillProceeds()
    {
        var (_, user) = await InvokeAsync(
            (ClerkUserId, false, PrincipalFor(ClerkUserId)),
            interceptor: new DuplicateKeyInterceptor());

        Assert.Equal(1, _nextCalls);
        Assert.False(user.IsAnonymous);
        Assert.Equal(ClerkUserId, user.UserId);
    }

    [Fact]
    public async Task WithoutDatabase_AuthenticatedRequestStillProceeds()
    {
        var (_, user) = await InvokeAsync((ClerkUserId, false, PrincipalFor(ClerkUserId)), registerDb: false);

        Assert.Equal(1, _nextCalls);
        Assert.False(user.IsAnonymous);
        Assert.Equal(ClerkUserId, user.UserId);
    }

    // ---- per-user cache ---------------------------------------------------------------------

    [Fact]
    public async Task SecondRequestForSameUser_IsServedFromCache()
    {
        await InvokeAsync((ClerkUserId, false, PrincipalFor(ClerkUserId)));
        await using (var db = OpenDb())
        {
            db.AppUsers.RemoveRange(db.AppUsers);
            await db.SaveChangesAsync();
        }

        await InvokeAsync((ClerkUserId, false, PrincipalFor(ClerkUserId)));

        Assert.Equal(2, _nextCalls);
        await using var check = OpenDb();
        Assert.Empty(check.AppUsers); // cache hit: no provisioning lookup ran again
    }

    [Fact]
    public async Task DifferentUsers_AreResolvedIndependently()
    {
        await InvokeAsync((ClerkUserId, false, PrincipalFor(ClerkUserId)));
        await InvokeAsync(("user_other", false, PrincipalFor("user_other")));

        await using var db = OpenDb();
        var ids = await db.AppUsers.Select(u => u.ClerkUserId).OrderBy(id => id).ToListAsync();
        Assert.Equal(["user_2abc", "user_other"], ids);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private async Task<(DefaultHttpContext Context, AppUserContext User)> InvokeAsync(
        (string? userId, bool isAnonymous, ClaimsPrincipal? principal) identity,
        bool registerDb = true,
        SaveChangesInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        if (registerDb)
        {
            services.AddDbContext<PrivatePrepDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
                if (interceptor is not null)
                    options.AddInterceptors(interceptor);
            });
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var user = new AppUserContext();
        var middleware = new UserResolutionMiddleware(_ =>
        {
            _nextCalls++;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx, ClerkReturning(identity), user, _cache, NullLogger<UserResolutionMiddleware>.Instance);

        return (ctx, user);
    }

    private static ClerkAuthService ClerkReturning((string? userId, bool isAnonymous, ClaimsPrincipal? principal) identity)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Development");
        var clerk = new Mock<ClerkAuthService>(
            new ConfigurationBuilder().Build(), env.Object, NullLogger<ClerkAuthService>.Instance);
        clerk.Setup(c => c.ExtractUserIdAsync(It.IsAny<HttpRequest>())).ReturnsAsync(identity);
        return clerk.Object;
    }

    private static ClaimsPrincipal PrincipalFor(string userId, string? email = null)
    {
        var identity = new ClaimsIdentity("Clerk");
        identity.AddClaim(new Claim("sub", userId));
        if (email is not null)
            identity.AddClaim(new Claim("email", email));
        return new ClaimsPrincipal(identity);
    }

    private PrivatePrepDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<PrivatePrepDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task SeedAsync(AppUserEntity entity)
    {
        await using var db = OpenDb();
        db.AppUsers.Add(entity);
        await db.SaveChangesAsync();
    }

    private static void AssertRecent(DateTime value) =>
        Assert.InRange(value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

    /// <summary>Simulates the unique-key violation a concurrent first request would cause.</summary>
    private sealed class DuplicateKeyInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated unique violation from a concurrent insert.");
    }
}
