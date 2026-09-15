using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Profile;
using PrivatePrep.Services.Tracking;

namespace PrivatePrep.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AgentController(
    IAnalyzeService analyzeService,
    ICareerProfileReader profileReader,
    UsageService usageService,
    IAppUserContext userContext,
    TokenTrackingService tokenTrackingService,
    ILogger<AgentController> logger) : ControllerBase
{
    public const int MaxJobDescriptionChars = AnalyzeService.MaxJobDescriptionLength;

    private void AppendDailyUsageAndTokenTrackingHeaders()
    {
        var u = usageService.GetBackendInfo();
        Response.Headers["X-Daily-Usage-Effective-Storage"] = u.EffectiveStorage;
        Response.Headers["X-Daily-Usage-Configured-Storage"] = u.ConfiguredUsageStorage;
        if (u.Degraded)
        {
            Response.Headers["X-Daily-Usage-Degraded"] = "true";
            if (!string.IsNullOrEmpty(u.DegradedReason))
                Response.Headers["X-Daily-Usage-Degraded-Reason"] = u.DegradedReason;
        }

        var t = tokenTrackingService.GetBackendInfo();
        Response.Headers["X-Token-Usage-Effective-Storage"] = t.EffectiveStorage;
        Response.Headers["X-Token-Usage-Configured-Storage"] = t.ConfiguredTokenUsageStorage;
        if (t.Degraded)
        {
            Response.Headers["X-Token-Usage-Degraded"] = "true";
            if (!string.IsNullOrEmpty(t.DegradedReason))
                Response.Headers["X-Token-Usage-Degraded-Reason"] = t.DegradedReason;
        }
    }

    [HttpPost("analyze")]
    [EnableRateLimiting("agent_chat")]
    public async Task<ActionResult<AnalyzeReport>> Analyze([FromBody] AnalyzeRequestDto? request)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var jd = request?.JobDescription?.Trim() ?? "";
        if (jd.Length < AnalyzeService.MinimumJobDescriptionLength)
            return BadRequest(new { error = "jd_too_short", message = "JD zu kurz" });
        if (jd.Length > MaxJobDescriptionChars)
            return BadRequest(new { error = "jd_too_long", message = $"Stellenanzeige zu lang (max. {MaxJobDescriptionChars} Zeichen)." });

        UsageCheckResult usageCheck;
        try
        {
            usageCheck = await usageService.CheckAndIncrementAsync(userId, isAnonymous: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Usage check failed (storage). UserId {UserId}", userId);
            return StatusCode(503, new
            {
                error = "usage_check_failed",
                message = "Usage service temporarily unavailable. Please retry.",
            });
        }

        if (!usageCheck.Allowed)
        {
            logger.LogInformation(
                "Usage limit reached. UserId {UserId} Plan {Plan} UsageToday {UsageToday} DailyLimit {DailyLimit}",
                userId, usageCheck.Plan, usageCheck.UsageToday, usageCheck.DailyLimit);
            return StatusCode(429, new
            {
                error = "usage_limit_reached",
                reason = usageCheck.Reason,
                message = usageCheck.Message,
                usageToday = usageCheck.UsageToday,
                dailyLimit = usageCheck.DailyLimit,
                plan = usageCheck.Plan,
            });
        }

        Response.Headers.Append("X-Usage-Today", usageCheck.UsageToday.ToString());
        Response.Headers.Append("X-Usage-Limit", usageCheck.DailyLimit == int.MaxValue ? "unlimited" : usageCheck.DailyLimit.ToString());
        Response.Headers.Append("X-Usage-Plan", usageCheck.Plan);
        AppendDailyUsageAndTokenTrackingHeaders();

        try
        {
            var profile = await profileReader.GetProfile(userId).ConfigureAwait(false);
            var cvColumn = await profileReader.GetCvRawTextAsync(userId, HttpContext.RequestAborted).ConfigureAwait(false);
            var cv = PickCvText(cvColumn, profile?.CvRawText);
            var story = profile?.Story ?? "";

            var report = await analyzeService
                .AnalyzeAsync(new AnalyzeRequest(userId, cv, story, jd), HttpContext.RequestAborted)
                .ConfigureAwait(false);

            await FireTokenTrackingAsync(userId, report).ConfigureAwait(false);
            return Ok(report);
        }
        catch (AnalyzeException ex)
        {
            logger.LogWarning(ex, "Analyze rejected. UserId {UserId} Code {Code}", userId, ex.ErrorCode);
            return ex.ErrorCode switch
            {
                "jd_too_short" => BadRequest(new { error = ex.ErrorCode, message = ex.Message }),
                "profile_incomplete" => BadRequest(new { error = ex.ErrorCode, message = ex.Message }),
                "llm_parse_failed" => StatusCode(500, new { error = ex.ErrorCode, message = ex.Message }),
                "llm_unavailable" => StatusCode(502, new { error = ex.ErrorCode, message = ex.Message }),
                _ => StatusCode(500, new { error = "analyze_error", message = "An internal error occurred. Please try again." }),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analyze execution failed. UserId {UserId}", userId);
            return StatusCode(500, new { error = "analyze_error", message = "An internal error occurred. Please try again." });
        }
    }

    [HttpGet("usage")]
    [EnableRateLimiting("agent_read")]
    public async Task<IActionResult> GetUsage()
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var lookupKey = isAnonymous ? $"anon:{userId}" : userId;
            string plan;
            int usage;
            if (isAnonymous)
            {
                plan = "anonymous";
                usage = await usageService.GetUsageTodayStrictAsync(lookupKey);
            }
            else
            {
                (plan, usage) = await usageService.GetUsageSnapshotAsync(userId);
            }
            var limit = UsageService.GetDailyLimit(plan);

            return Ok(new
            {
                plan,
                usageToday = usage,
                dailyLimit = limit,
                responsesLeft = limit == int.MaxValue ? int.MaxValue : Math.Max(0, limit - usage),
                isAnonymous,
                isUnlimited = limit == int.MaxValue,
                resetsAt = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read usage and plan for user {UserId}", userId);
            return StatusCode(503, new
            {
                error = "usage_read_failed",
                message = "Failed to read usage and plan from storage. Please retry.",
            });
        }
    }

    private static string PickCvText(string? column, string? jsonField)
    {
        var a = column?.Trim() ?? "";
        var b = jsonField?.Trim() ?? "";
        return a.Length >= b.Length ? a : b;
    }

    private async Task FireTokenTrackingAsync(string userId, AnalyzeReport report)
    {
        var i = report.InputTokens ?? 0;
        var o = report.OutputTokens ?? 0;
        if (i == 0 && o == 0)
            return;

        var model = report.ModelUsed ?? "unknown";
        try
        {
            await tokenTrackingService.TrackUsageAsync(userId, "jobanalyzer", model, i, o, 0, 0).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token tracking failed for user {UserId}", userId);
        }
    }
}
