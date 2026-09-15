using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace PrivatePrep.Services.SkillGap;

/// <summary>
/// Word-boundary, case-insensitive mention check so "Java" does not match inside "JavaScript".
/// </summary>
internal static class SkillMention
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool AppearsIn(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            return false;

        var regex = Cache.GetOrAdd(token.Trim(), static t =>
        {
            var escaped = Regex.Escape(t);
            return new Regex(
                $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(150));
        });

        return regex.IsMatch(text);
    }

    public static bool AnyAppearsIn(string text, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (AppearsIn(text, token))
                return true;
        }

        return false;
    }
}
