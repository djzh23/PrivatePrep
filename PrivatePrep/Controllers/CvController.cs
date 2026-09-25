using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrivatePrep.Models;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Auth;
using PrivatePrep.Services.Privacy;
using PrivatePrep.Services.Profile;

namespace PrivatePrep.Controllers;

[ApiController]
[Route("api/profile/cv")]
public sealed class CvController(
    CareerProfileService profileService,
    ICvUploadService cvUploadService,
    IPiiScrubberService piiScrubber,
    IAppUserContext userContext,
    ILlmSingleCompletionService llmSingleCompletion,
    CvParsingService cvParsingService,
    ILogger<CvController> logger) : ControllerBase
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

    private static bool HasEnoughForAnonymousSummary(CareerProfile p, string? cvText)
    {
        if ((p.Skills?.Count ?? 0) > 0)
            return true;
        if ((p.Experience?.Count ?? 0) > 0)
            return true;
        var cv = cvText?.Trim();
        return cv is { Length: >= 50 };
    }

    private static string BuildAnonymousCvSummaryPrompt(CareerProfile p, bool isEnglish, string? cvText)
    {
        var sb = new StringBuilder();
        if (isEnglish)
        {
            sb.AppendLine("You write a factual English prose summary (5–12 sentences) for an AI assistant user profile.");
            sb.AppendLine("STRICT: No person names, no postal addresses, no phone numbers, no email addresses, no URLs, no company names.");
            sb.AppendLine("Use neutral wording such as \"a mid-sized software company\" instead of specific employers.");
            sb.AppendLine("Describe work experience without naming specific organizations.");
            sb.AppendLine();
            sb.AppendLine("Structured input (may contain sensitive fields — do NOT copy literally into the output):");
        }
        else
        {
            sb.AppendLine("Du erstellst einen sachlichen deutschen Fließtext (5–12 Sätze) für ein KI-Assistenz-Profil.");
            sb.AppendLine("STRIKT: Keine Personennamen, keine Adressen, keine Telefonnummern, keine E-Mail-Adressen, keine URLs, keine Firmennamen.");
            sb.AppendLine("Nutze neutrale Formulierungen wie \"eine mittelständische Softwarefirma\" statt konkreter Unternehmen.");
            sb.AppendLine("Beschreibe Berufserfahrung ohne konkrete Arbeitgeber.");
            sb.AppendLine();
            sb.AppendLine("Strukturierte Input-Daten (ggf. mit sensiblen Spalten — NICHT wörtlich übernehmen):");
        }

        if (!string.IsNullOrWhiteSpace(p.FieldLabel))
            sb.AppendLine($"Berufsfeld: {p.FieldLabel}");
        if (!string.IsNullOrWhiteSpace(p.LevelLabel))
            sb.AppendLine($"Erfahrungslevel: {p.LevelLabel}");
        if (!string.IsNullOrWhiteSpace(p.CurrentRole))
            sb.AppendLine($"Aktuelle Rolle (Text kann Namen enthalten — NICHT ausgeben): {Truncate(p.CurrentRole, 300)}");

        if (p.Goals.Count > 0)
            sb.AppendLine("Ziele: " + string.Join("; ", p.Goals.Take(12)));

        if (p.Skills.Count > 0)
            sb.AppendLine("Skills: " + string.Join(", ", p.Skills.Take(40)));

        if (p.Experience.Count > 0)
        {
            sb.AppendLine("Berufserfahrung (Unternehmen NICHT im Output nennen):");
            foreach (var e in p.Experience.Take(8))
            {
                sb.Append(" - ");
                if (!string.IsNullOrWhiteSpace(e.Title))
                    sb.Append($"{e.Title.Trim()}. ");
                if (!string.IsNullOrWhiteSpace(e.Duration))
                    sb.Append($"Zeitraum: {e.Duration.Trim()}. ");
                if (!string.IsNullOrWhiteSpace(e.Summary))
                    sb.Append(Truncate(e.Summary.Trim(), 400));
                sb.AppendLine();
            }
        }

        if (p.EducationEntries.Count > 0)
        {
            sb.AppendLine("Ausbildung (Institution nicht nennen wenn nicht nötig — evtl. \"Studium\" ohne Ortsnamen):");
            foreach (var ed in p.EducationEntries.Take(6))
            {
                sb.Append(" - ");
                if (!string.IsNullOrWhiteSpace(ed.Degree))
                    sb.Append(ed.Degree.Trim());
                if (!string.IsNullOrWhiteSpace(ed.Year))
                    sb.Append($" ({ed.Year.Trim()})");
                sb.AppendLine();
            }
        }

        if (p.Languages.Count > 0)
        {
            sb.AppendLine("Sprachen: "
                + string.Join(", ", p.Languages
                    .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                    .Take(12)
                    .Select(l => string.IsNullOrWhiteSpace(l.Level) ? l.Name!.Trim() : $"{l.Name!.Trim()} ({l.Level.Trim()})")));
        }

        var rawCv = cvText?.Trim();
        if (!string.IsNullOrEmpty(rawCv))
        {
            sb.AppendLine();
            sb.AppendLine(isEnglish
                ? "CV raw text (truncated to 6000 chars, may contain PII — do not output):"
                : "CV-Rohtext (auf 6000 Zeichen gekürzt, kann PII enthalten — nicht ausgeben):");
            sb.AppendLine(Truncate(rawCv, 6000));
        }
        sb.AppendLine();
        sb.AppendLine(isEnglish
            ? "Answer with the prose only: no heading, no leading bullet or number."
            : "Antwort nur mit dem Fließtext, ohne Überschrift, ohne Aufzählungszeichen am Anfang.");
        return sb.ToString();
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    [HttpPost]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> UploadCv([FromBody] UploadCvRequest request)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "CV-Text darf nicht leer sein." });

        CvUploadResult registered;
        try
        {
            registered = await cvUploadService
                .RegisterTextAsync(request.Text, userId, HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // Pasted text gets the same structured extraction as a PDF upload (skills, experience,
        // education, languages) instead of only registering the hash. ParseCvWithAi never throws —
        // on any LLM failure it returns an empty ParsedCvData, so this never blocks registration.
        var parsed = await cvParsingService
            .ParseCvWithAi(piiScrubber.ScrubBestEffort(request.Text), p => llmSingleCompletion.CompleteAsync(p, 2000, HttpContext.RequestAborted))
            .ConfigureAwait(false);

        SetCareerProfileStorageHeaders();
        return Ok(new
        {
            success = true,
            contentHash = registered.ContentHash,
            contentLength = registered.ContentLength,
            extractedText = registered.ExtractedText,
            parsed,
        });
    }

    /// <summary>
    /// Deletes CV-derived career data and the stored fingerprint. Analysis is blocked until a new CV is uploaded.
    /// </summary>
    [HttpDelete]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> ClearCvDerivedData()
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            await profileService.ClearCvDerivedDataAsync(userId, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            SetCareerProfileStorageHeaders();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Clear CV-derived profile data failed for user {UserId}", userId);
            return StatusCode(500, new { error = "cv_clear_failed", message = "Lebenslauf-Daten konnten nicht gelöscht werden." });
        }
    }

    /// <summary>
    /// PDF-CV hochladen: Text extrahieren, per KI strukturieren, Hash speichern. Rohtext geht nicht in die Datenbank.
    /// </summary>
    [HttpPost("upload-pdf")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> UploadCvPdf([FromBody] UploadCvPdfRequest request)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (string.IsNullOrEmpty(request.Base64Pdf))
            return BadRequest(new { error = "PDF-Daten fehlen." });

        if (request.Base64Pdf.Length > 5_000_000)
            return BadRequest(new { error = "PDF darf maximal 5MB groß sein." });

        try
        {
            string rawText;
            try
            {
                rawText = cvParsingService.ExtractTextFromPdf(request.Base64Pdf);
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "Ungültige Base64-Daten." });
            }

            if (string.IsNullOrWhiteSpace(rawText) || rawText.Length < 50)
                return BadRequest(new { error = "Konnte keinen Text aus der PDF extrahieren. Ist es ein Bild-PDF?" });

            var parsed = await cvParsingService
                .ParseCvWithAi(
                    piiScrubber.ScrubBestEffort(rawText),
                    p => llmSingleCompletion.CompleteAsync(p, 2000, HttpContext.RequestAborted))
                .ConfigureAwait(false);

            var registered = await cvUploadService
                .RegisterTextAsync(rawText, userId, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            SetCareerProfileStorageHeaders();
            return Ok(new
            {
                success = true,
                contentHash = registered.ContentHash,
                contentLength = registered.ContentLength,
                extractedText = registered.ExtractedText,
                parsed,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CV PDF upload failed for user {UserId}", userId);
            return BadRequest(new { error = "PDF-Verarbeitung fehlgeschlagen. Bitte versuche es erneut." });
        }
    }

    /// <summary>
    /// Optionaler Profil-Fließtext für KI-Kontext. Prompt verlangt, Namen und Kontaktdaten nicht auszugeben.
    /// </summary>
    [HttpPost("anonymous-summary")]
    [EnableRateLimiting("cv_summary")]
    public async Task<IActionResult> AnonymousCvSummary([FromBody] AnonymousCvSummaryRequest? request)
    {
        var userId = userContext.UserId;
        var isAnonymous = userContext.IsAnonymous;
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        var lang = request?.Language?.Trim().ToLowerInvariant();
        var isEnglish = lang is "en" or "english";

        CareerProfile? profile;
        try
        {
            profile = await profileService.GetProfile(userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Anonymous CV summary: profile load failed for {UserId}", userId);
            return StatusCode(503, new
            {
                error = "profile_load_failed",
                message = "Profil konnte nicht geladen werden. Bitte später erneut versuchen.",
            });
        }

        profile ??= new CareerProfile { UserId = userId };
        NormalizeProfileLists(profile);
        var cvText = piiScrubber.ScrubBestEffort(request?.CvText ?? "");

        if (!HasEnoughForAnonymousSummary(profile, cvText))
        {
            return BadRequest(new
            {
                error = "insufficient_data",
                message = "Trage mindestens Skills, eine Berufserfahrung oder einen CV-Text (mind. 50 Zeichen) ein.",
            });
        }

        var prompt = BuildAnonymousCvSummaryPrompt(profile, isEnglish, cvText);
        try
        {
            var text = await llmSingleCompletion
                .CompleteAsync(prompt, 720, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return StatusCode(502, new
                {
                    error = "llm_empty",
                    message = "Die KI hat keinen Text zurückgegeben.",
                });
            }

            var trimmed = text.Trim();
            if (trimmed.Length > CareerProfileStorageLimits.CvSummaryMaxChars)
                trimmed = trimmed[..CareerProfileStorageLimits.CvSummaryMaxChars];

            return Ok(new AnonymousCvSummaryResponse(trimmed));
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "cancelled", message = "Anfrage abgebrochen." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Anonymous CV summary LLM failed for user {UserId}", userId);
            return StatusCode(502, new
            {
                error = "llm_failed",
                message = "Zusammenfassung konnte nicht erstellt werden. Bitte später erneut versuchen.",
            });
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
}
