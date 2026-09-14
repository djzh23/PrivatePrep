using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrivatePrep.Models;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Profile;
using PrivatePrep.Services.Background;
using PrivatePrep.Services.VectorStore;

namespace PrivatePrep.Controllers;

[ApiController]
[Route("api/profile")]
public sealed class ProfileController(
    CareerProfileService profileService,
    IAppUserContext userContext,
    IAgentBackgroundQueue backgroundQueue,
    ILogger<ProfileController> logger) : ControllerBase
{
    private static void QueueProfileIngestion(
        IAgentBackgroundQueue queue,
        ILogger logger,
        string userId,
        CareerProfile profile)
    {
        queue.TryEnqueue("profile-ingestion", async (sp, ct) =>
        {
            try
            {
                var ingester = sp.GetRequiredService<ICareerMemoryIngester>();
                await ingester.IngestProfileAsync(userId, profile, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Profile memory ingestion failed for user {UserId}", userId);
            }
        });
    }

    private void SetCareerProfileStorageHeaders()
    {
        var info = profileService.GetBackendInfo();
        Response.Headers["X-Career-Profile-Effective-Storage"] = info.EffectiveStorage;
        Response.Headers["X-Career-Profile-Configured-Storage"] = info.ConfiguredCareerProfileStorage;
        if (info.Degraded)
        {
            Response.Headers["X-Career-Profile-Degraded"] = "true";
            if (!string.IsNullOrEmpty(info.DegradedReason))
                Response.Headers["X-Career-Profile-Degraded-Reason"] = info.DegradedReason;
        }
    }

    private static void NormalizeProfileLists(CareerProfile profile)
    {
        profile.Goals ??= new List<string>();
        profile.Skills ??= new List<string>();
        profile.Experience ??= new List<WorkExperience>();
        profile.EducationEntries ??= new List<Education>();
        profile.Languages ??= new List<ProfileLanguageEntry>();
        profile.TargetJobs ??= new List<TargetJob>();
    }

    [HttpGet]
    [EnableRateLimiting("agent_read")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var profile = await profileService.GetProfile(userId);
            SetCareerProfileStorageHeaders();
            return Ok(profile ?? new CareerProfile { UserId = userId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GET /api/profile failed for user {UserId}", userId);
            return StatusCode(503, new
            {
                error = "profile_read_failed",
                message = "Could not load profile from storage. Please retry.",
            });
        }
    }

    [HttpPut("skills")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> UpdateSkills([FromBody] UpdateSkillsRequest request)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SetSkills(userId, request.Skills ?? new List<string>());
        var skillsProfile = await profileService.GetProfile(userId).ConfigureAwait(false);
        if (skillsProfile is not null)
            QueueProfileIngestion(backgroundQueue, logger, userId, skillsProfile);
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }

    [HttpPost("target-jobs")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> AddTargetJob([FromBody] AddTargetJobRequest request)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var jobId = await profileService.AddTargetJob(userId, request.Title, request.Company, request.Description);
            SetCareerProfileStorageHeaders();
            return Ok(new { success = true, id = jobId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("target-jobs/{jobId}")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> RemoveTargetJob(string jobId)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.RemoveTargetJob(userId, jobId);
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }

    [HttpPut]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> UpdateProfile([FromBody] CareerProfile profile)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        profile.UserId = userId;
        NormalizeProfileLists(profile);

        if (profile.TargetJobs.Count > 3)
            return BadRequest(new { error = "Maximal 3 Wunschstellen erlaubt." });
        if (profile.Skills.Count > 30)
            return BadRequest(new { error = "Maximal 30 Skills erlaubt." });

        try
        {
            await profileService.SaveProfile(userId, profile);
            QueueProfileIngestion(backgroundQueue, logger, userId, profile);
            SetCareerProfileStorageHeaders();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save career profile for user {UserId}", userId);
            return StatusCode(500, new { error = "profile_save_failed" });
        }
    }
}
