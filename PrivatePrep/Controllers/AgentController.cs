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
            return BadRequest(new { error = "jd_too_short", message = "Die Stellenanzeige ist zu kurz." });
        if (jd.Length > MaxJobDescriptionChars)
            return BadRequest(new { error = "jd_too_long", message = $"Stellenanzeige zu lang (max. {MaxJobDescriptionChars} Zeichen)." });

        var cvCheck = ValidateAnalyzeCv(request);
        if (cvCheck is not null)
            return cvCheck;

        var cvText = request?.CvText?.Trim() ?? "";
        var providedHash = request?.CvContentHash?.Trim() ?? "";

        CvFingerprint? storedFingerprint;
        try
        {
            storedFingerprint = await profileReader
                .GetCvFingerprintAsync(userId, HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CV fingerprint load failed. UserId {UserId}", userId);
            return StatusCode(503, new
            {
                error = "profile_load_failed",
                message = "Profil konnte nicht geladen werden. Bitte später erneut versuchen.",
            });
        }

        if (storedFingerprint is null)
            return BadRequest(new { error = "cv_not_uploaded", message = "Bitte zuerst einen Lebenslauf hochladen." });
        if (!CvContentHasher.HexEquals(providedHash, storedFingerprint.Value.ContentHash))
            return BadRequest(new { error = "cv_stale", message = "Der Lebenslauf in diesem Browser stimmt nicht mit dem letzten Upload überein. Bitte erneut hochladen." });

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
            var story = profile?.Story ?? "";

            var report = await analyzeService
                .AnalyzeAsync(new AnalyzeRequest(userId, cvText, story, jd), HttpContext.RequestAborted)
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
                "cv_missing" => BadRequest(new { error = ex.ErrorCode, message = ex.Message }),
                "cv_hash_invalid" => BadRequest(new { error = ex.ErrorCode, message = ex.Message }),
                "cv_hash_mismatch" => BadRequest(new { error = ex.ErrorCode, message = ex.Message }),
                "cv_not_uploaded" => BadRequest(new { error = ex.ErrorCode, message = ex.Message }),
                "cv_stale" => BadRequest(new { error = ex.ErrorCode, message = ex.Message }),
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

    private BadRequestObjectResult? ValidateAnalyzeCv(AnalyzeRequestDto? request)
    {
        var cv = request?.CvText?.Trim() ?? "";
        if (cv.Length == 0)
            return BadRequest(new { error = "cv_missing", message = "Lebenslauf-Text fehlt. Bitte den Lebenslauf in diesem Browser erneut hochladen." });

        var provided = request?.CvContentHash?.Trim() ?? "";
        if (!CvContentHasher.IsSha256Hex(provided))
            return BadRequest(new { error = "cv_hash_invalid", message = "Prüfwert des Lebenslaufs ist ungültig." });

        var computed = CvContentHasher.Sha256Hex(cv);
        if (!CvContentHasher.HexEquals(computed, provided))
            return BadRequest(new { error = "cv_hash_mismatch", message = "Prüfwert und Lebenslauf-Text passen nicht zusammen." });

        return null;
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
