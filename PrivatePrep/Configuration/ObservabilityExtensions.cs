using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Context;
using PrivatePrep.Data;

namespace PrivatePrep.Configuration;

public static class ObservabilityExtensions
{
    /// <param name="registerPostgresCheck">When true, adds <see cref="PrivatePrepDbContext"/> connectivity check.</param>
    public static IServiceCollection AddPrivatePrepHealthChecks(this IServiceCollection services, bool registerPostgresCheck = false)
    {
        var checks = services.AddHealthChecks();
        if (registerPostgresCheck)
            checks.AddDbContextCheck<PrivatePrepDbContext>("postgres");
        return services;
    }

    public static IApplicationBuilder UseRequestId(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var header = context.Request.Headers["X-Request-Id"].ToString();
            var id = string.IsNullOrWhiteSpace(header) ? Guid.NewGuid().ToString("N") : header.Trim();
            context.Response.Headers["X-Request-Id"] = id;
            using (LogContext.PushProperty("RequestId", id))
                await next();
        });
    }
}
