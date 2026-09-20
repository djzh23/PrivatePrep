using System.Text.RegularExpressions;

namespace PrivatePrep.Services.Privacy;

public sealed class PiiScrubberService : IPiiScrubberService
{
    public const string EmailPlaceholder = "[E-MAIL ENTFERNT]";
    public const string PhonePlaceholder = "[TELEFON ENTFERNT]";
    public const string UrlPlaceholder = "[LINK ENTFERNT]";

    private static readonly Regex UrlRegex = new(
        @"https?://\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IntlPhoneRegex = new(
        @"\+?49[\s\-]?\d{2,4}[\s\-]?\d{3,4}[\s\-]?\d{3,4}",
        RegexOptions.Compiled);

    private static readonly Regex NationalPhoneRegex = new(
        @"\b0\d{2,4}[\s\-/]?\d{3,4}[\s\-]?\d{3,7}\b",
        RegexOptions.Compiled);

    public string ScrubBestEffort(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var text = UrlRegex.Replace(input, UrlPlaceholder);
        text = EmailRegex.Replace(text, EmailPlaceholder);
        text = IntlPhoneRegex.Replace(text, PhonePlaceholder);
        text = NationalPhoneRegex.Replace(text, PhonePlaceholder);
        return text;
    }
}
