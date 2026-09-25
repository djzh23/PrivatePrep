using Microsoft.AspNetCore.Mvc;
using PrivatePrep.Models;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Inbox;
using PrivatePrep.Services.Reports;

namespace PrivatePrep.Controllers;

/// <summary>
/// Inbox of job postings collected by the browser extension ("job vacuum") or entered manually.
/// The extension only collects; the user reviews here and the app analyzes when they choose to.
/// Reachable from the extension's chrome-extension:// origin via the shared "PrivatePrepWeb"
/// CORS policy (Program.cs), which every controller already uses via RequireCors.
/// </summary>
[ApiController]
[Route("api/inbox")]
public sealed class InboxController(
    IInboxService inboxService,
    IAnalysisReportService reportService,
    IAppUserContext userContext,
    ILogger<InboxController> logger) : ControllerBase
{
    public const int MaxActiveJobs = 100;
    public const int MaxNewJobsPerWindow = 30;
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(60);
    public const int MinRawTextLength = 100;
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 100;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInboxJobRequest? request, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var validationError = ValidateCreate(request);
        if (validationError is not null)
            return BadRequest(validationError);

        if (await inboxService.CountActiveForUserAsync(userId, ct).ConfigureAwait(false) >= MaxActiveJobs)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "inbox_full",
                message = "Inbox ist voll. Bitte loesche oder analysiere alte Eintraege.",
            });
        }

        if (await inboxService.CountRecentForUserAsync(userId, RecentWindow, ct).ConfigureAwait(false) >= MaxNewJobsPerWindow)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "rate_limited",
                message = "Zu viele Speicherungen in kurzer Zeit. Bitte warte einen Moment.",
            });
        }

        try
        {
            var job = await inboxService.CreateOrUpdateAsync(userId, request!, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(GetById), new { id = job.Id }, ToResponse(job));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inbox create failed for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "inbox_save_failed",
                message = "Eintrag konnte nicht gespeichert werden. Bitte spaeter erneut versuchen.",
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? limit, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        InboxJobStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<InboxJobStatus>(status, ignoreCase: true, out var s))
            {
                return BadRequest(new
                {
                    error = "invalid_status",
                    message = "Ungueltiger Status. Erlaubt sind New, Analyzed oder Archived.",
                });
            }
            parsedStatus = s;
        }

        var effectiveLimit = Math.Clamp(limit ?? DefaultListLimit, 1, MaxListLimit);
        var jobs = await inboxService.ListForUserAsync(userId, parsedStatus, effectiveLimit, ct).ConfigureAwait(false);
        return Ok(jobs.Select(ToListItemResponse).ToList());
    }

    [HttpGet("count")]
    public async Task<IActionResult> Count([FromQuery] string? status, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var parsedStatus = InboxJobStatus.New;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<InboxJobStatus>(status, ignoreCase: true, out var s))
            {
                return BadRequest(new
                {
                    error = "invalid_status",
                    message = "Ungueltiger Status. Erlaubt sind New, Analyzed oder Archived.",
                });
            }
            parsedStatus = s;
        }

        var count = await inboxService.CountForUserAsync(userId, parsedStatus, ct).ConfigureAwait(false);
        return Ok(new InboxCountResponse(count, parsedStatus.ToString()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var job = await inboxService.GetByIdForUserAsync(userId, id, ct).ConfigureAwait(false);
        if (job is null)
            return NotFound(new { error = "inbox_job_not_found", message = "Eintrag wurde nicht gefunden." });

        return Ok(ToResponse(job));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInboxJobRequest? request, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var validationError = ValidateUpdate(request);
        if (validationError is not null)
            return BadRequest(validationError);

        try
        {
            var job = await inboxService.UpdateAsync(userId, id, request ?? new UpdateInboxJobRequest(null, null, null), ct)
                .ConfigureAwait(false);
            return Ok(ToResponse(job));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "inbox_job_not_found", message = "Eintrag wurde nicht gefunden." });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var deleted = await inboxService.DeleteAsync(userId, id, ct).ConfigureAwait(false);
        if (!deleted)
            return NotFound(new { error = "inbox_job_not_found", message = "Eintrag wurde nicht gefunden." });

        return NoContent();
    }

    [HttpGet("{id:guid}/report")]
    public async Task<IActionResult> GetReport(Guid id, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var job = await inboxService.GetByIdForUserAsync(userId, id, ct).ConfigureAwait(false);
        if (job is null)
            return NotFound(new { error = "inbox_job_not_found", message = "Eintrag wurde nicht gefunden." });

        var report = await reportService.GetByInboxJobIdForUserAsync(userId, id, ct).ConfigureAwait(false);
        if (report is null)
            return NotFound(new { error = "report_not_found", message = "Dieser Job wurde noch nicht analysiert." });

        return Ok(ToReportResponse(report));
    }

    private static object? ValidateCreate(CreateInboxJobRequest? request)
    {
        if (request is null)
            return new { error = "invalid_request", message = "Anfrage ist leer." };
        if (string.IsNullOrWhiteSpace(request.Title))
            return new { error = "title_required", message = "Titel darf nicht leer sein." };
        if (string.IsNullOrWhiteSpace(request.Company))
            return new { error = "company_required", message = "Firma darf nicht leer sein." };
        if (string.IsNullOrWhiteSpace(request.RawText) || request.RawText.Trim().Length < MinRawTextLength)
            return new { error = "raw_text_too_short", message = $"Stellentext muss mindestens {MinRawTextLength} Zeichen haben." };
        if (!IsValidHttpUrl(request.SourceUrl))
            return new { error = "invalid_source_url", message = "SourceUrl ist keine gueltige Adresse." };
        return null;
    }

    private static object? ValidateUpdate(UpdateInboxJobRequest? request)
    {
        if (request is null)
            return null;
        if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title))
            return new { error = "title_required", message = "Titel darf nicht leer sein." };
        if (request.Company is not null && string.IsNullOrWhiteSpace(request.Company))
            return new { error = "company_required", message = "Firma darf nicht leer sein." };
        if (request.RawText is not null && request.RawText.Trim().Length < MinRawTextLength)
            return new { error = "raw_text_too_short", message = $"Stellentext muss mindestens {MinRawTextLength} Zeichen haben." };
        return null;
    }

    private static bool IsValidHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static InboxJobResponse ToResponse(Models.InboxJob job) => new(
        job.Id,
        job.Title,
        job.Company,
        job.Location,
        job.SourceUrl,
        job.SourceKind,
        job.RawText,
        job.Status,
        job.ExtractedAt,
        job.AnalyzedAt,
        job.AnalysisReportId);

    private static AnalysisReportResponse ToReportResponse(Models.AnalysisReport report) => new(
        report.Id,
        report.InboxJobId,
        report.ReportJson,
        report.CvHash,
        report.JdHash,
        report.CvLength,
        report.JdLength,
        report.LlmModel,
        report.MatchScore,
        report.CreatedAt,
        report.UpdatedAt);

    private static InboxJobListItemResponse ToListItemResponse(Models.InboxJob job) => new(
        job.Id,
        job.Title,
        job.Company,
        job.Location,
        job.SourceUrl,
        job.SourceKind,
        job.Status,
        job.ExtractedAt,
        job.AnalyzedAt,
        job.AnalysisReportId,
        job.RawText.Length <= InboxService.RawTextPreviewLength
            ? job.RawText
            : job.RawText[..InboxService.RawTextPreviewLength]);
}
