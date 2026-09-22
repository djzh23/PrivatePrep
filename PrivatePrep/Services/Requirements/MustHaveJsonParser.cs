using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivatePrep.Services.Requirements;

internal static class MustHaveJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parses the model's raw completion into proposals. An item with an unknown kind/status or a missing
    /// posting quote is skipped rather than failing the whole response — one bad item should not discard the
    /// rest. Returns false only when the response has no recognisable JSON object at all.
    /// </summary>
    public static bool TryParse(string raw, out List<ProposedMustHave> proposals)
    {
        proposals = [];
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var json = ExtractJsonObject(raw);
        if (json is null)
            return false;

        try
        {
            var dto = JsonSerializer.Deserialize<MustHaveListDto>(json, JsonOptions);
            if (dto?.MustHaves is null)
                return false;

            foreach (var item in dto.MustHaves)
            {
                if (string.IsNullOrWhiteSpace(item.PostingQuote))
                    continue;
                if (!Enum.TryParse<RequirementKind>(item.Kind ?? "", ignoreCase: true, out var kind))
                    continue;
                if (!Enum.TryParse<MustHaveStatus>(item.Status ?? "", ignoreCase: true, out var status))
                    continue;

                proposals.Add(new ProposedMustHave(
                    kind,
                    item.PostingQuote.Trim(),
                    status,
                    string.IsNullOrWhiteSpace(item.CvQuote) ? null : item.CvQuote.Trim()));
            }

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

    private sealed class MustHaveListDto
    {
        [JsonPropertyName("must_haves")]
        public List<MustHaveDto>? MustHaves { get; set; }
    }

    private sealed class MustHaveDto
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("posting_quote")]
        public string? PostingQuote { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("cv_quote")]
        public string? CvQuote { get; set; }
    }
}
