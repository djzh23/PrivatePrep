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
                    b.Reasoning ?? "")).ToList());
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
        List<BulletRewriteSuggestion> bullets)
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
    }

    private sealed class LlmAnalyzeDto
    {
        [JsonPropertyName("global_score")]
        public decimal GlobalScore { get; set; }

        [JsonPropertyName("dimensions")]
        public LlmDimensionsDto? Dimensions { get; set; }

        [JsonPropertyName("role_summary")]
        public string? RoleSummary { get; set; }

        [JsonPropertyName("culture_screen")]
        public string? CultureScreen { get; set; }

        [JsonPropertyName("warnings")]
        public List<string>? Warnings { get; set; }

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

    private sealed class LlmBulletDto
    {
        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("rewritten")]
        public string? Rewritten { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }
}
