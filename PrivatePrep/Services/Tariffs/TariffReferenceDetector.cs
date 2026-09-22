using System.Text.RegularExpressions;

namespace PrivatePrep.Services.Tariffs;

/// <param name="Group">Entgeltgruppe as named in the posting, whitespace removed (e.g. "E11", "S8a", "P8", "9a", "6").</param>
public sealed record TariffReference(TariffAgreement Agreement, string Group);

public interface ITariffReferenceDetector
{
    /// <summary>Finds the posting's tariff citation (Entgeltgruppe + agreement), if it states one unambiguously.</summary>
    TariffReference? Detect(string? postingText);
}

/// <summary>
/// Finds a spelled-out "Entgeltgruppe &lt;code&gt;" and, only if a TVöD or TV-L mention sits close by (spec R1:
/// evidence, not guesswork), pairs the two. A lone "TVöD"/"TV-L" mention elsewhere in the posting (e.g. "sichere
/// Kenntnisse im TV-L") never counts on its own — only a real Entgeltgruppe citation triggers a match, and the
/// nearest agreement mention around it wins. The abbreviation "EG" is deliberately not matched: it also means
/// "Erdgeschoss" in German postings, and a false tariff citation is worse than a missed one (AC-2: no invented
/// facts) — a miss here still degrades gracefully to "keine Tarifdaten" in the report, never a wrong one.
/// </summary>
public sealed partial class TariffReferenceDetector : ITariffReferenceDetector
{
    private const int ContextWindow = 40;

    public TariffReference? Detect(string? postingText)
    {
        if (string.IsNullOrWhiteSpace(postingText))
            return null;

        foreach (Match groupMatch in GroupCodeRegex().Matches(postingText))
        {
            var agreement = FindNearbyAgreement(postingText, groupMatch);
            if (agreement is null)
                continue;

            return new TariffReference(agreement.Value, NormalizeGroup(groupMatch.Groups["group"].Value));
        }

        return null;
    }

    private static TariffAgreement? FindNearbyAgreement(string text, Match groupMatch)
    {
        var afterStart = groupMatch.Index + groupMatch.Length;
        var after = text[afterStart..Math.Min(text.Length, afterStart + ContextWindow)];
        var afterMatch = AgreementRegex().Match(after);
        if (afterMatch.Success)
            return ParseAgreement(afterMatch.Value);

        var beforeStart = Math.Max(0, groupMatch.Index - ContextWindow);
        var before = text[beforeStart..groupMatch.Index];
        var beforeMatches = AgreementRegex().Matches(before);
        return beforeMatches.Count > 0 ? ParseAgreement(beforeMatches[^1].Value) : null;
    }

    private static TariffAgreement ParseAgreement(string raw) =>
        raw.Contains("TV-L", StringComparison.OrdinalIgnoreCase) ? TariffAgreement.TVL : TariffAgreement.TVoeD;

    private static string NormalizeGroup(string raw)
    {
        var compact = Whitespace().Replace(raw, "");
        return compact.Length > 0 && char.IsLetter(compact[0])
            ? char.ToUpperInvariant(compact[0]) + compact[1..]
            : compact;
    }

    [GeneratedRegex(@"Entgeltgruppe\s+(?<group>[EPS]?\s?\d{1,2}[a-zü]?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GroupCodeRegex();

    [GeneratedRegex(@"TVöD|TV-L", RegexOptions.IgnoreCase)]
    private static partial Regex AgreementRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
