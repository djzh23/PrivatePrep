using Microsoft.Extensions.Options;
using PrivatePrep.Configuration;
using PrivatePrep.Services.Auth;

namespace PrivatePrep.Middleware;

/// <summary>
/// When BetaMode is enabled, unauthenticated callers cannot use /api except health and Stripe webhooks.
/// Default is off so current deploys stay open until BETA_MODE=true is set.
/// </summary>
public sealed class BetaModeMiddleware(RequestDelegate next, IOptions<BetaModeOptions> options)
{
    public async Task InvokeAsync(HttpContext context, IAppUserContext user)
    {
        if (!options.Value.Enabled)
        {
            await next(context);
            return;
        }

        if (HttpMethods.IsOptions(context.Request.Method)
            || context.Request.Path.StartsWithSegments("/api/health")
            || context.Request.Path.StartsWithSegments("/api/stripe/webhook"))
        {
            await next(context);
            return;
        }

        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (user.IsAnonymous || string.IsNullOrWhiteSpace(user.UserId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"error":"beta_closed"}""");
            return;
        }

        await next(context);
    }
}
