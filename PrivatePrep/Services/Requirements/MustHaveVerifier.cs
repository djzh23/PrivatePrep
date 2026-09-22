using System.Text.RegularExpressions;

namespace PrivatePrep.Services.Requirements;

public enum MustHaveStatus
{
    Met,
    NotMet,
    Unclear,
}

/// <summary>
/// One must-have as the model proposes it (spec 001, AC-1 decision A). <paramref name="PostingQuote"/> is the
/// verbatim posting excerpt the requirement comes from; <paramref name="CvQuote"/> is the verbatim CV excerpt
/// backing a "Met" status, if any. Both are evidence, not code-verified until <see cref="IMustHaveVerifier"/> runs.
/// </summary>
public sealed record ProposedMustHave(RequirementKind Kind, string PostingQuote, MustHaveStatus Status, string? CvQuote = null);

/// <summary>A must-have whose posting quote is confirmed real. Its status has been code-checked (see remarks).</summary>
public sealed record VerifiedMustHave(RequirementKind Kind, string PostingQuote, MustHaveStatus Status);

public sealed record MustHaveVerificationResult(
    IReadOnlyList<VerifiedMustHave> Verified,
    IReadOnlyList<ProposedMustHave> Rejected);

public interface IMustHaveVerifier
{
    /// <summary>
    /// Verifies the model's proposed must-haves against the source texts. This is the code half of AC-1
    /// decision A ("the model proposes, code enforces"), matching career-ops's own split of responsibilities.
    /// </summary>
    MustHaveVerificationResult Verify(IReadOnlyList<ProposedMustHave> proposals, string postingText, string cvText);
}

/// <summary>
/// Two independent guards, both required by AC-2 ("no invented facts") and AC-3 ("claims need a quote"):
/// 1. A proposal whose posting quote cannot be found in the posting is discarded outright — the model
///    invented a requirement the posting never states.
/// 2. A "Met" claim without a matching CV quote is not discarded, only downgraded to "Unclear" — the
///    requirement itself is still real and belongs in the report, we just cannot vouch for the match.
/// "NotMet" and "Unclear" need no CV evidence: an absence cannot be quoted.
/// </summary>
public sealed partial class MustHaveVerifier : IMustHaveVerifier
{
    public MustHaveVerificationResult Verify(IReadOnlyList<ProposedMustHave> proposals, string postingText, string cvText)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        postingText ??= string.Empty;
        cvText ??= string.Empty;

        var verified = new List<VerifiedMustHave>();
        var rejected = new List<ProposedMustHave>();

        foreach (var proposal in proposals)
        {
            if (!QuoteAppearsIn(proposal.PostingQuote, postingText))
            {
                rejected.Add(proposal);
                continue;
            }

            var status = proposal.Status == MustHaveStatus.Met && !QuoteAppearsIn(proposal.CvQuote, cvText)
                ? MustHaveStatus.Unclear
                : proposal.Status;

            verified.Add(new VerifiedMustHave(proposal.Kind, proposal.PostingQuote, status));
        }

        return new MustHaveVerificationResult(verified, rejected);
    }

    private static bool QuoteAppearsIn(string? quote, string source) =>
        !string.IsNullOrWhiteSpace(quote) && Normalize(source).Contains(Normalize(quote), StringComparison.Ordinal);

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim().ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
