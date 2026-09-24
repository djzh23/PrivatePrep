using Microsoft.AspNetCore.Mvc;
using PrivatePrep.Models;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Inbox;
using PrivatePrep.Services.Reports;

namespace PrivatePrep.Controllers;

/// <summary>
/// Persisted analysis reports (PrivatePrep.Services.Agent.AnalyzeReport), created as a side effect
/// of POST /api/agent/analyze. See also InboxController.GetReport (GET /api/inbox/{id}/report) for
/// the inbox-job-scoped lookup.
/// </summary>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController(
    IAnalysisReportService reportService,
    IInboxService inboxService,
    IAppUserContext userContext,
    ILogger<ReportsController> logger) : ControllerBase
{
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 100;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var report = await reportService.GetByIdForUserAsync(userId, id, ct).ConfigureAwait(false);
        if (report is null)
            return NotFound(new { error = "report_not_found", message = "Bericht wurde nicht gefunden." });

        return Ok(ToResponse(report));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? limit, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        var effectiveLimit = Math.Clamp(limit ?? DefaultListLimit, 1, MaxListLimit);
        var reports = await reportService.ListForUserAsync(userId, effectiveLimit, ct).ConfigureAwait(false);

        // N+1 by design here, not a join in the service: capped at MaxListLimit (100) rows, and
        // spec 001's own volume NFR expects under 100 analyses per day total, so this is
        // negligible. Keeps IAnalysisReportService free of InboxJob-shaped concerns.
        var items = new List<AnalysisReportListItemResponse>(reports.Count);
        foreach (var report in reports)
        {
            string? title = null;
            string? company = null;
            if (report.InboxJobId is Guid jobId)
            {
                var job = await inboxService.GetByIdForUserAsync(userId, jobId, ct).ConfigureAwait(false);
                title = job?.Title;
                company = job?.Company;
            }
            items.Add(ToListItemResponse(report, title, company));
        }

        return Ok(items);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "Bitte anmelden." });

        try
        {
            var deleted = await reportService.DeleteAsync(userId, id, ct).ConfigureAwait(false);
            if (!deleted)
                return NotFound(new { error = "report_not_found", message = "Bericht wurde nicht gefunden." });

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Report delete failed for user {UserId}, report {ReportId}", userId, id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "report_delete_failed",
                message = "Bericht konnte nicht geloescht werden. Bitte spaeter erneut versuchen.",
            });
        }
    }

    private static AnalysisReportResponse ToResponse(Models.AnalysisReport report) => new(
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

    private static AnalysisReportListItemResponse ToListItemResponse(Models.AnalysisReport report, string? title, string? company) => new(
        report.Id,
        report.InboxJobId,
        report.MatchScore,
        report.CreatedAt,
        title,
        company);
}
