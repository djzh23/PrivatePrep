using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PrivatePrep.Data;
using PrivatePrep.Data.Entities;
using PrivatePrep.Services.Auth;

namespace PrivatePrep.Middleware;

/// <summary>
/// Resolves the authenticated user on every request.
/// - Extracts userId from the verified JWT (via ClerkAuthService)
/// - Ensures an app_users row exists (single provisioning point)
/// - Populates IAppUserContext for the request scope
///
/// This replaces all scattered EnsureAppUserAsync calls across services.
/// </summary>
public sealed class UserResolutionMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(
        HttpContext context,
        ClerkAuthService authService,
        IAppUserContext appUserContext,
        IMemoryCache cache,
        ILogger<UserResolutionMiddleware> logger)
    {
        var userCtx = (AppUserContext)appUserContext;
        var (userId, isAnonymous, principal) = await authService.ExtractUserIdAsync(context.Request);

        userCtx.UserId = userId ?? "";
        userCtx.IsAnonymous = isAnonymous;
        if (principal is not null)
            context.User = principal;

        if (!isAnonymous && !string.IsNullOrWhiteSpace(userId))
            await EnsureProvisionedAsync(context, cache, logger, userId);

        await next(context);
    }

    /// <summary>
    /// Creates the app_users row on a user's first request. Memoized per user so the lookup
    /// does not hit the database on every request.
    /// </summary>
    private static async Task EnsureProvisionedAsync(
        HttpContext context,
        IMemoryCache cache,
        ILogger<UserResolutionMiddleware> logger,
        string userId)
    {
        var cacheKey = $"user_provisioned:{userId}";
        if (cache.TryGetValue(cacheKey, out _))
            return;

        var db = context.RequestServices.GetService<PrivatePrepDbContext>();
        if (db is null)
            return;

        var exists = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.ClerkUserId == userId, context.RequestAborted);

        if (!exists)
        {
            var now = DateTime.UtcNow;
            var newUser = new AppUserEntity
            {
                ClerkUserId = userId,
                CreatedAt = now,
                UpdatedAt = now,
                LastActiveAt = now,
            };

            try
            {
                db.AppUsers.Add(newUser);
                await db.SaveChangesAsync(context.RequestAborted);
                logger.LogInformation("New user provisioned via middleware. UserId {UserId}", userId);
            }
            catch (DbUpdateException)
            {
                // Concurrent insert race: row already exists, which is fine
                db.Entry(newUser).State = EntityState.Detached;
            }
        }

        cache.Set(cacheKey, true, CacheDuration);
    }
}
