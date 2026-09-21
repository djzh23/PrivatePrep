using Microsoft.Extensions.Options;
using PrivatePrep.Configuration;
using PrivatePrep.Services.Auth;

namespace PrivatePrep.Middleware;

/// <summary>
/// When BetaMode is enabled, authenticated callers must present a Clerk JWT
/// <c>email</c> claim that is on the allow-list. Anonymous callers pass through
/// so controllers keep their normal 401. OPTIONS, health, and the Stripe webhook
/// skip the check.
/// </summary>
public sealed class BetaModeMiddleware(
    RequestDelegate next,
    IOptions<BetaModeOptions> options,
    ILogger<BetaModeMiddleware> logger)
{
    private readonly HashSet<string> _allowedEmails = ParseEmails(options.Value.AllowedEmails);

    public async Task InvokeAsync(HttpContext context, IAppUserContext user)
    {
        if (!options.Value.Enabled)
        {
            await next(context);
            return;
        }

        if (IsPublicPath(context))
        {
            await next(context);
            return;
        }

        if (user.IsAnonymous || string.IsNullOrWhiteSpace(user.UserId))
        {
            await next(context);
            return;
        }

        var email = context.User.FindFirst("email")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !_allowedEmails.Contains(email))
        {
            logger.LogInformation("Beta gate blocked user with email {Email}", email ?? "unknown");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "beta_access_required",
                message = "Diese Anwendung ist aktuell nur für eingeladene Beta-Tester verfügbar. Für Zugang: zn.connec.team@gmail.com",
            });
            return;
        }

        await next(context);
    }

    private static bool IsPublicPath(HttpContext context)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
            return true;

        var path = context.Request.Path;
        return path.StartsWithSegments("/api/health")
            || path.StartsWithSegments("/api/stripe/webhook");
    }

    private static HashSet<string> ParseEmails(string? raw)
    {
        return (raw ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
