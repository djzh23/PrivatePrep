using System.Text.RegularExpressions;

namespace PrivatePrep.Services.Infrastructure;

/// <summary>
/// Normalizes inbound user text before LLM calls.
/// </summary>
public static class UserInputCleaner
{
    public static string CleanUserInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? string.Empty;

        var s = input.Trim();
        s = Regex.Replace(s, "<[^>]+>", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        s = Regex.Replace(s, @"[ \t]{2,}", " ");
        s = s.Trim();

        const int maxLen = 4000;
        if (s.Length > maxLen)
            s = s[..maxLen] + "\n[Text gekürzt]";

        return s;
    }
}
