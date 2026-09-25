using System.Text.Json;
using System.Text.Json.Serialization;
using PrivatePrep.Services.FactGate;
using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Services.Agent;

internal static class AnalyzeJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static bool TryParse(string raw, out LlmAnalyzePayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var json = ExtractJsonObject(raw);
        if (json is null)
            return false;

        try
        {
            var dto = JsonSerializer.Deserialize<LlmAnalyzeDto>(json, JsonOptions);
            if (dto is null)
                return false;

            payload = new LlmAnalyzePayload(
                dto.GlobalScore,
                dto.Dimensions?.CvMatch ?? 0,
                dto.Dimensions?.RoleAlignment ?? 0,
                dto.Dimensions?.Culture ?? 0,
                dto.Dimensions?.RedFlags ?? 1,
                dto.RoleSummary ?? "",
                dto.CultureScreen ?? "not_evaluated",
                dto.Warnings ?? [],
                (dto.BulletRewrites ?? []).Select(b => new BulletRewriteSuggestion(
                    b.Original ?? "",
                    b.Rewritten ?? "",
                    b.Reasoning ?? "",
                    b.EvidenceLine)).ToList(),
                dto.DimensionReasons is null
                    ? null
                    : new LlmDimensionReasons(
                        dto.DimensionReasons.CvMatch ?? "",
                        dto.DimensionReasons.RoleAlignment ?? "",
                        dto.DimensionReasons.Culture ?? "",
                        dto.DimensionReasons.RedFlags ?? ""),
                dto.VerdictHeadline,
                dto.VerdictParagraph,
                (dto.SectionFindings ?? []).Select(f => new LlmSectionFinding(
                    f.Section ?? "",
                    f.Label ?? "",
                    f.Observation ?? "",
                    f.Action ?? "")).ToList(),
                (dto.ActionPlan ?? []).Select(a => new LlmActionPlanItem(
                    a.Priority,
                    a.Action ?? "",
                    a.EffortMinutes,
                    a.Impact ?? "")).ToList());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        return raw[start..(end + 1)];
    }

    internal sealed class LlmAnalyzePayload(
        decimal globalScore,
        decimal cvMatch,
        decimal roleAlignment,
        decimal culture,
        decimal redFlags,
        string roleSummary,
        string cultureScreen,
        List<string> warnings,
        List<BulletRewriteSuggestion> bullets,
        LlmDimensionReasons? dimensionReasons,
        string? verdictHeadline,
        string? verdictParagraph,
        List<LlmSectionFinding> sectionFindings,
        List<LlmActionPlanItem> actionPlan)
    {
        public decimal GlobalScore { get; } = globalScore;
        public decimal CvMatch { get; } = cvMatch;
        public decimal RoleAlignment { get; } = roleAlignment;
        public decimal Culture { get; } = culture;
        public decimal RedFlags { get; } = redFlags;
        public string RoleSummary { get; } = roleSummary;
        public string CultureScreen { get; } = cultureScreen;
        public List<string> Warnings { get; } = warnings;
        public List<BulletRewriteSuggestion> Bullets { get; } = bullets;
        public LlmDimensionReasons? DimensionReasons { get; } = dimensionReasons;
        public string? VerdictHeadline { get; } = verdictHeadline;
        public string? VerdictParagraph { get; } = verdictParagraph;
        public List<LlmSectionFinding> SectionFindings { get; } = sectionFindings;
        public List<LlmActionPlanItem> ActionPlan { get; } = actionPlan;
    }

    internal sealed record LlmDimensionReasons(string CvMatch, string RoleAlignment, string Culture, string RedFlags);

    internal sealed record LlmSectionFinding(string Section, string Label, string Observation, string Action);

    internal sealed record LlmActionPlanItem(int Priority, string Action, int? EffortMinutes, string Impact);

    private sealed class LlmAnalyzeDto
    {
        [JsonPropertyName("global_score")]
        public decimal GlobalScore { get; set; }

        [JsonPropertyName("dimensions")]
        public LlmDimensionsDto? Dimensions { get; set; }

        [JsonPropertyName("dimension_reasons")]
        public LlmDimensionReasonsDto? DimensionReasons { get; set; }

        [JsonPropertyName("role_summary")]
        public string? RoleSummary { get; set; }

        [JsonPropertyName("verdict_headline")]
        public string? VerdictHeadline { get; set; }

        [JsonPropertyName("verdict_paragraph")]
        public string? VerdictParagraph { get; set; }

        [JsonPropertyName("culture_screen")]
        public string? CultureScreen { get; set; }

        [JsonPropertyName("warnings")]
        public List<string>? Warnings { get; set; }

        [JsonPropertyName("section_findings")]
        public List<LlmSectionFindingDto>? SectionFindings { get; set; }

        [JsonPropertyName("action_plan")]
        public List<LlmActionPlanItemDto>? ActionPlan { get; set; }

        [JsonPropertyName("bullet_rewrites")]
        public List<LlmBulletDto>? BulletRewrites { get; set; }
    }

    private sealed class LlmDimensionsDto
    {
        [JsonPropertyName("cv_match")]
        public decimal CvMatch { get; set; }

        [JsonPropertyName("role_alignment")]
        public decimal RoleAlignment { get; set; }

        [JsonPropertyName("culture")]
        public decimal Culture { get; set; }

        [JsonPropertyName("red_flags")]
        public decimal RedFlags { get; set; }
    }

    private sealed class LlmDimensionReasonsDto
    {
        [JsonPropertyName("cv_match")]
        public string? CvMatch { get; set; }

        [JsonPropertyName("role_alignment")]
        public string? RoleAlignment { get; set; }

        [JsonPropertyName("culture")]
        public string? Culture { get; set; }

        [JsonPropertyName("red_flags")]
        public string? RedFlags { get; set; }
    }

    private sealed class LlmSectionFindingDto
    {
        [JsonPropertyName("section")]
        public string? Section { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("observation")]
        public string? Observation { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }
    }

    private sealed class LlmActionPlanItemDto
    {
        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("effort_minutes")]
        public int? EffortMinutes { get; set; }

        [JsonPropertyName("impact")]
        public string? Impact { get; set; }
    }

    private sealed class LlmBulletDto
    {
        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("rewritten")]
        public string? Rewritten { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }

        [JsonPropertyName("evidence_line")]
        public string? EvidenceLine { get; set; }
    }
}
