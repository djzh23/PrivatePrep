using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Polly;
using Serilog;
using PrivatePrep.Configuration;
using PrivatePrep.Data;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Applications;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.FactGate;
using PrivatePrep.Services.Groq;
using PrivatePrep.Services.Infrastructure;
using PrivatePrep.Services.Payments;
using PrivatePrep.Services.Profile;
using PrivatePrep.Services.SkillGap;
using PrivatePrep.Services.Tracking;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});
builder.Configuration.AddUserSecrets<Program>(optional: true);

var frontendFromEnv = Environment.GetEnvironmentVariable("FRONTEND__BASEURL")?.Trim();
if (!string.IsNullOrWhiteSpace(frontendFromEnv))
    builder.Configuration["Frontend:BaseUrl"] = frontendFromEnv;

var clerkIssuer = Environment.GetEnvironmentVariable("CLERK__ISSUER")
    ?? Environment.GetEnvironmentVariable("CLERK_ISSUER");
if (!string.IsNullOrWhiteSpace(clerkIssuer)) builder.Configuration["Clerk:Issuer"] = clerkIssuer;

var groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
if (!string.IsNullOrWhiteSpace(groqKey)) builder.Configuration["Groq:ApiKey"] = groqKey;
var groqModel = Environment.GetEnvironmentVariable("GROQ_MODEL");
if (!string.IsNullOrWhiteSpace(groqModel)) builder.Configuration["Groq:Model"] = groqModel;

var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort))
{
    builder.WebHost.UseUrls($"http://*:{renderPort}");
}

var localOrigins = new[]
{
    "http://localhost:5194",
    "http://localhost:5108",
    "http://localhost:5000",
    "http://localhost:7000",
    "https://localhost:7001",
    "http://localhost:5173",
    "http://localhost:5174",
    "http://localhost:5175",
    "http://localhost:5176",
};
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var envOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var frontendBaseUrl = builder.Configuration["Frontend:BaseUrl"]?.Trim();
var frontendOrigin = Array.Empty<string>();
if (!string.IsNullOrWhiteSpace(frontendBaseUrl))
{
    try
    {
        var u = new Uri(frontendBaseUrl, UriKind.Absolute);
        frontendOrigin = [u.GetLeftPart(UriPartial.Authority)];
    }
    catch
    {
        // invalid URL — skip; startup log still lists explicit CORS origins
    }
}

var allowedOrigins = localOrigins
    .Concat(configuredOrigins)
    .Concat(envOrigins)
    .Concat(frontendOrigin)
    .Where(static o => !string.IsNullOrWhiteSpace(o))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddControllers();
builder.Services.Configure<DatabaseFeatureOptions>(builder.Configuration.GetSection(DatabaseFeatureOptions.SectionName));
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<DatabaseFeatureOptions>, DatabaseFeatureOptionsValidator>();

var supabaseResolution = SupabaseConnectionString.Resolve(builder.Configuration);
var supabaseConnectionString = supabaseResolution.ConnectionString;
if (supabaseConnectionString is not null)
{
    builder.Configuration.AddInMemoryCollection(
    [
        new KeyValuePair<string, string?>("ConnectionStrings:Supabase", supabaseConnectionString),
        new KeyValuePair<string, string?>("ConnectionStrings:Postgres", supabaseConnectionString),
    ]);
}

var registerPostgres = !string.IsNullOrWhiteSpace(supabaseConnectionString);
if (registerPostgres)
{
    builder.Services.AddDbContext<PrivatePrepDbContext>(options =>
        options.UseNpgsql(supabaseConnectionString, npgsql => npgsql.UseVector()));
    builder.Services.AddScoped<CareerProfilePostgresService>();
    builder.Services.AddScoped<UsagePostgresService>();
    builder.Services.AddScoped<TokenTrackingPostgresService>();
    builder.Services.AddScoped<IUsageTrackingService, UsageTrackingService>();
    builder.Services.AddScoped<UsageService>();
    builder.Services.AddScoped<TokenTrackingService>();
    builder.Services.AddScoped<CareerProfileService>();
    builder.Services.AddScoped<ICareerProfileReader>(sp => sp.GetRequiredService<CareerProfileService>());
}

var databaseFeaturesPreview = builder.Configuration.GetSection(DatabaseFeatureOptions.SectionName)
    .Get<DatabaseFeatureOptions>() ?? new DatabaseFeatureOptions();
var registerPostgresHealth = registerPostgres && databaseFeaturesPreview.PostgresEnabled;
builder.Services.AddPrivatePrepHealthChecks(registerPostgresCheck: registerPostgresHealth);
builder.Services.AddPrivatePrepRateLimiter();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection(GroqOptions.SectionName));
builder.Services.AddHttpClient<GroqChatCompletionService>(client =>
{
    client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
    client.Timeout = TimeSpan.FromSeconds(90);
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 2;
    options.Retry.UseJitter = true;
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.Delay = TimeSpan.FromMilliseconds(500);
    options.Retry.ShouldHandle = args => ValueTask.FromResult(
        args.Outcome.Exception is HttpRequestException
        || (args.Outcome.Result is { } resp
            && ((int)resp.StatusCode >= 500
                || resp.StatusCode == System.Net.HttpStatusCode.RequestTimeout
                || resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)));
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(90);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(180);
});
builder.Services.AddHostedService<PrivatePrepMigrationRunner>();
builder.Services.AddSingleton(SkillTaxonomyCatalog.LoadEmbedded());
builder.Services.AddSingleton<ISkillGapService, SkillGapService>();
builder.Services.AddSingleton<IFactGateService, FactGateService>();
builder.Services.AddScoped<IJobContextExtractor, JobContextExtractor>();
builder.Services.AddScoped<CvParsingService>();
builder.Services.AddScoped<ILlmRouter, GroqLlmRouter>();
builder.Services.AddScoped<IAnalyzeService, AnalyzeService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<IAgentService>(sp => sp.GetRequiredService<AgentService>());
builder.Services.AddScoped<ILlmSingleCompletionService, AgentLlmSingleCompletionService>();
builder.Services.AddSingleton<ClerkAuthService>();
builder.Services.AddScoped<AppUserContext>();
builder.Services.AddScoped<IAppUserContext>(sp => sp.GetRequiredService<AppUserContext>());
builder.Services.AddScoped<IStripeApiClient, StripeApiClient>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("PrivatePrepWeb", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .AllowAnyHeader()
            .WithExposedHeaders(
                "X-Usage-Today",
                "X-Usage-Limit",
                "X-Usage-Plan",
                "X-Request-Id",
                "X-Career-Profile-Effective-Storage",
                "X-Career-Profile-Configured-Storage",
                "X-Career-Profile-Degraded",
                "X-Career-Profile-Degraded-Reason",
                "X-Daily-Usage-Effective-Storage",
                "X-Daily-Usage-Configured-Storage",
                "X-Daily-Usage-Degraded",
                "X-Daily-Usage-Degraded-Reason",
                "X-Token-Usage-Effective-Storage",
                "X-Token-Usage-Configured-Storage",
                "X-Token-Usage-Degraded",
                "X-Token-Usage-Degraded-Reason");
    });
});

var app = builder.Build();

using (var warmupScope = app.Services.CreateScope())
{
    var clerkAuth = warmupScope.ServiceProvider.GetRequiredService<ClerkAuthService>();
    await clerkAuth.WarmupAsync();
}

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation("CORS allowed origins: {Origins}", string.Join(", ", allowedOrigins));
if (supabaseConnectionString is not null)
{
    try
    {
        var parsed = new NpgsqlConnectionStringBuilder(supabaseConnectionString);
        startupLogger.LogInformation(
            "Supabase: Npgsql connection resolved from {SourceKey}. Host={Host}; Database={Database}; SSL={SslMode}. (password not logged)",
            supabaseResolution.SourceKey ?? "unknown",
            parsed.Host,
            parsed.Database,
            parsed.SslMode);
    }
    catch
    {
        startupLogger.LogInformation(
            "Supabase: Npgsql connection resolved from {SourceKey}. (password not logged)",
            supabaseResolution.SourceKey ?? "unknown");
    }
}
else
{
    startupLogger.LogWarning(
        "Supabase: no EF connection registered. SourceKey={SourceKey}; Reason={Reason}. "
        + "Check Render env DATABASE_URL or SUPABASE__CONNECTIONSTRING (two underscores) or ConnectionStrings__Supabase.",
        supabaseResolution.SourceKey ?? "(none)",
        supabaseResolution.RejectReason ?? "unknown");
}

app.UseRouting();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnhandledException");
        logger.LogError(ex, "Unhandled exception");
        if (!context.Response.HasStarted)
        {
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                context.Response.Headers["Vary"] = "Origin";
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"internal_error\"}");
        }
    }
});

app.UseCors("PrivatePrepWeb");

app.UseRequestId();
app.UseSerilogRequestLogging();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/stripe/webhook"))
        context.Request.EnableBuffering();

    await next();
});

app.UseRateLimiter();
app.UsePrivatePrepSecurityHeaders();
app.UseStaticFiles();
app.UseMiddleware<PrivatePrep.Middleware.UserResolutionMiddleware>();

app.MapHealthChecks("/api/health");
app.MapControllers().RequireCors("PrivatePrepWeb");

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
