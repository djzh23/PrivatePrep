using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrivatePrep.Configuration;
using PrivatePrep.Models;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Tracking;

namespace PrivatePrep.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AgentController(
    IAgentService agentService,
    UsageService usageService,
    IAppUserContext userContext,
    TokenTrackingService tokenTrackingService,
    ILogger<AgentController> logger) : ControllerBase
{
    private async Task<(ActionResult? Error, AgentRequest Normalized)> TryNormalizeAgentRequestAsync(
        AgentRequest request,
        string scopeUserId,
        bool isAnonymous,
        bool enforcePlan)
    {
        var resolved = AgentToolResolution.TryResolve(request.ToolType);
        if (resolved is null)
        {
            return (BadRequest(new { error = "unknown_tool", message = "Unbekanntes Werkzeug." }), request);
        }

        var skill = resolved.Skill;
        if (!skill.IsEnabled)
        {
            return (StatusCode(403, new
            {
                error = "coming_soon",
                message = $"{skill.Name} ist bald verfügbar.",
            }), request);
        }

        if (enforcePlan)
        {
            var plan = isAnonymous ? "anonymous" : await usageService.GetPlanAsync(scopeUserId);
            if (!SkillRegistry.IsToolAccessible(plan, skill))
            {
                return (StatusCode(403, new
                {
                    error = "plan_required",
                    message = "Für dieses Werkzeug ist ein höherer Tarif nötig.",
                }), request);
            }
        }

        var truncatedSetup = AgentPayloadLimits.TruncateCareerSetup(request.CareerToolSetup);
        var probe = request with { CareerToolSetup = truncatedSetup };
        var payloadErr = AgentPayloadLimits.ValidateTotalPayload(probe);
        if (payloadErr is not null)
        {
            return (BadRequest(new
            {
                error = payloadErr,
                message = payloadErr == "payload_too_large"
                    ? "Gesamtgröße von Nachricht und Setup-Feldern zu groß."
                    : "Nachricht zu lang.",
            }), request);
        }

        var normalized = request with
        {
            ToolType = resolved.ApiToolType,
            CareerProfileUserId = isAnonymous ? null : scopeUserId,
            ConversationScopeUserId = scopeUserId,
            JobApplicationId = isAnonymous ? null : request.JobApplicationId,
            CareerToolSetup = truncatedSetup,
        };

        return (null, normalized);
    }

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

    [HttpPost("ask")]
    [EnableRateLimiting("agent_chat")]
    public async Task<ActionResult<AgentResponse>> Ask([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "message_empty", message = "Message must not be empty." });

        var askPayloadProbe = request with { CareerToolSetup = AgentPayloadLimits.TruncateCareerSetup(request.CareerToolSetup) };
        if (AgentPayloadLimits.ValidateTotalPayload(askPayloadProbe) is { } askPayloadErr)
        {
            return BadRequest(new
            {
                error = askPayloadErr,
                message = askPayloadErr == "payload_too_large"
                    ? "Gesamtgröße von Nachricht und Setup-Feldern zu groß."
                    : $"Message must not exceed {AgentPayloadLimits.MaxMessageChars} characters.",
            });
        }

        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_failed", message = "Invalid authentication token." });

        logger.LogInformation(
            "Ask request. UserId {UserId} IsAnonymous {IsAnonymous} ToolType {ToolType}",
            userId, isAnonymous, request.ToolType ?? "jobanalyzer");

        UsageCheckResult usageCheck;
        try
        {
            usageCheck = await usageService.CheckAndIncrementAsync(userId, isAnonymous);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Usage check failed (storage). UserId {UserId} IsAnonymous {IsAnonymous}",
                userId, isAnonymous);
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
            var (normErr, agentRequest) = await TryNormalizeAgentRequestAsync(request, userId, isAnonymous, true);
            if (normErr != null)
                return normErr;

            var result = await agentService.RunAsync(agentRequest);
            await FireTokenTrackingAsync(userId, agentRequest.ToolType, result).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Agent execution failed. UserId {UserId} ToolType {ToolType}",
                userId, request.ToolType ?? "jobanalyzer");
            return StatusCode(500, new { error = "agent_error", message = "An internal error occurred. Please try again." });
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

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });

    [HttpPost("demo")]
    [EnableRateLimiting("agent_chat")]
    public async Task<ActionResult<AgentResponse>> Demo([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "message_empty" });

        if (request.Message.Length > 4000)
            return BadRequest(new { error = "message_too_long" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var demoUserId = $"demo_agent:{ip}";
        const int demoLimit = 5;

        int currentUsage;
        try
        {
            currentUsage = await usageService.GetUsageTodayAsync(demoUserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Demo usage check failed for IP {IP}", ip);
            return StatusCode(503, new { error = "usage_check_failed" });
        }

        if (currentUsage >= demoLimit)
            return StatusCode(429, new
            {
                error = "demo_limit_reached",
                reason = "demo_limit",
                message = "Demo-Limit erreicht. Melde dich an für 3 kostenlose Analysen pro Tag.",
            });

        try
        {
            var (normErr, normalizedDemo) = await TryNormalizeAgentRequestAsync(request, demoUserId, isAnonymous: false, enforcePlan: false);
            if (normErr is not null)
                return normErr;

            await usageService.IncrementUsageAsync(demoUserId);
            var result = await agentService.RunAsync(normalizedDemo);
            await FireTokenTrackingAsync(demoUserId, normalizedDemo.ToolType, result).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo agent execution failed. IP {IP} ToolType {ToolType}", ip, request.ToolType);
            return StatusCode(500, new { error = "agent_error", message = "An internal error occurred. Please try again." });
        }
    }

    private async Task FireTokenTrackingAsync(string userId, string? toolTypeRaw, AgentResponse result)
    {
        var tool = string.IsNullOrWhiteSpace(toolTypeRaw) ? "jobanalyzer" : toolTypeRaw.ToLowerInvariant();
        if (result.InputTokens is not { } i || result.OutputTokens is not { } o)
            return;

        var cc = result.CacheCreationInputTokens ?? 0;
        var cr = result.CacheReadInputTokens ?? 0;
        if (i == 0 && o == 0 && cc == 0 && cr == 0)
            return;

        var model = result.Model ?? "unknown";
        try
        {
            await tokenTrackingService.TrackUsageAsync(userId, tool, model, i, o, cc, cr).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token tracking failed for user {UserId}", userId);
        }
    }
}
