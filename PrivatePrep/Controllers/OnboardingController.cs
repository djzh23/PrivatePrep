using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrivatePrep.Models;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Profile;

namespace PrivatePrep.Controllers;

[ApiController]
[Route("api/profile/onboarding")]
public sealed class OnboardingController(
    CareerProfileService profileService,
    IAppUserContext userContext) : ControllerBase
{
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

    [HttpPost]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> CompleteOnboarding([FromBody] OnboardingRequest request)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SetOnboarding(
            userId,
            request.Field,
            request.FieldLabel,
            request.Level,
            request.LevelLabel,
            request.CurrentRole,
            request.Goals ?? new List<string>());
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }

    /// <summary>Onboarding-Entwurf abrufen — gibt leeres Objekt zurück wenn kein Entwurf existiert.</summary>
    [HttpGet("draft")]
    [EnableRateLimiting("agent_read")]
    public async Task<IActionResult> GetOnboardingDraft()
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        var draft = await profileService.GetOnboardingDraftAsync(userId);
        SetCareerProfileStorageHeaders();
        return Ok(draft ?? new OnboardingDraft());
    }

    /// <summary>Onboarding-Entwurf speichern — setzt onboarding_completed NICHT.</summary>
    [HttpPut("draft")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> SaveOnboardingDraft([FromBody] SaveOnboardingDraftRequest request)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        var draft = new OnboardingDraft
        {
            Field = request.Field?.Trim(),
            Level = request.Level?.Trim(),
            CurrentRole = request.CurrentRole?.Trim(),
            Goals = request.Goals,
            LastStep = request.LastStep,
        };

        await profileService.SaveOnboardingDraftAsync(userId, draft);
        SetCareerProfileStorageHeaders();
        return NoContent();
    }

    /// <summary>Coach-Tour als abgeschlossen markieren — Flag wird einmalig gesetzt.</summary>
    [HttpPost("coach-tour/done")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> CompleteCoachTour()
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SetCoachTourCompletedAsync(userId);
        SetCareerProfileStorageHeaders();
        return NoContent();
    }

    /// <summary>Onboarding überspringen — markiert das Profil als abgeschlossen ohne Pflichtdaten.</summary>
    [HttpPost("skip")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> SkipOnboarding()
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SkipOnboardingAsync(userId);
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }
}
