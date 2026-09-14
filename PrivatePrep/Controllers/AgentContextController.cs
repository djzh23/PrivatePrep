using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrivatePrep.Models;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Chat;

namespace PrivatePrep.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentContextController(
    ConversationService conversationService,
    IAppUserContext userContext,
    ILogger<AgentContextController> logger) : ControllerBase
{
    [HttpPost("context")]
    [EnableRateLimiting("agent_chat")]
    public async Task<IActionResult> SetContext([FromBody] SetContextRequest request)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in to set context." });

        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest(new { error = "SessionId required." });

        var toolType = string.IsNullOrWhiteSpace(request.ToolType)
            ? "general"
            : request.ToolType.ToLowerInvariant();

        try
        {
            if (request.CVText is not null || request.JobTitle is not null || request.CompanyName is not null)
            {
                await conversationService.UpdateContextAsync(
                    userId,
                    request.SessionId,
                    toolType,
                    ctx =>
                    {
                        if (request.CVText is not null)
                            ctx.UserCV = request.CVText;
                        if (request.JobTitle is not null)
                            ctx.InterviewJobTitle = request.JobTitle;
                        if (request.CompanyName is not null)
                            ctx.InterviewCompany = request.CompanyName;
                    });
            }

            if (request.ProgrammingLanguage is not null)
            {
                await conversationService.UpdateContextAsync(
                    userId,
                    request.SessionId,
                    toolType,
                    ctx => ctx.ProgrammingLanguage = request.ProgrammingLanguage);
            }

            return Ok(new { success = true, sessionId = request.SessionId, toolType });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update agent context. SessionId {SessionId} ToolType {ToolType}", request.SessionId, toolType);
            return StatusCode(500, new { error = "context_update_failed" });
        }
    }

    [HttpGet("context/{sessionId}/{toolType}")]
    [EnableRateLimiting("agent_read")]
    public async Task<IActionResult> GetContext(string sessionId, string toolType)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "SessionId required." });

        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in to read context." });

        var normalizedToolType = string.IsNullOrWhiteSpace(toolType)
            ? "general"
            : toolType.ToLowerInvariant();

        try
        {
            var context = await conversationService.GetContextAsync(userId, sessionId, normalizedToolType);
            return Ok(new
            {
                sessionId = context.SessionId,
                toolType = context.ToolType,
                conversationLanguage = context.ConversationLanguage,
                hasJob = context.Job?.IsAnalyzed == true,
                jobTitle = context.Job?.JobTitle,
                companyName = context.Job?.CompanyName,
                hasCV = !string.IsNullOrWhiteSpace(context.UserCV),
                // userCV is intentionally omitted — never expose raw CV text over the API
                interviewJobTitle = context.InterviewJobTitle,
                interviewCompany = context.InterviewCompany,
                hasProgrammingLang = !string.IsNullOrWhiteSpace(context.ProgrammingLanguage),
                programmingLanguage = context.ProgrammingLanguage,
                userFacts = context.UserFacts,
                practisedQuestions = context.PractisedQuestions,
                lastActivity = context.LastActivity,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read agent context. SessionId {SessionId} ToolType {ToolType}", sessionId, normalizedToolType);
            return StatusCode(500, new { error = "context_read_failed" });
        }
    }
}
