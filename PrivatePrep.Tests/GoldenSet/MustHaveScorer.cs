using System.Text.RegularExpressions;

namespace PrivatePrep.Tests.GoldenSet;

public sealed record MustHaveScore(int Expected, int Found, int Correct, int FalsePositives)
{
    /// <summary>Share of expected requirements that were found AND assessed correctly (AC-1: at least 0.95).</summary>
    public double Accuracy => Expected == 0 ? 1.0 : (double)Correct / Expected;

    /// <summary>Share of expected requirements that were found at all, whatever their status. Isolates extraction from the CV comparison.</summary>
    public double Recall => Expected == 0 ? 1.0 : (double)Found / Expected;
}

/// <summary>The outcome of pairing expected and actual requirements one to one.</summary>
public sealed record MustHaveMatch(
    int Found,
    int Correct,
    IReadOnlyList<ExpectedMustHave> Missed,
    IReadOnlyList<ExpectedMustHave> Extra);

/// <summary>Compares the requirements an extractor found with the golden expectations.</summary>
public static partial class MustHaveScorer
{
    public static MustHaveScore Score(IReadOnlyList<ExpectedMustHave> expected, IReadOnlyList<ExpectedMustHave> actual)
    {
        var match = Match(expected, actual);
        return new MustHaveScore(expected.Count, match.Found, match.Correct, match.Extra.Count);
    }

    /// <summary>Pairs each expected item with at most one actual item of the same kind and overlapping quote.</summary>
    public static MustHaveMatch Match(IReadOnlyList<ExpectedMustHave> expected, IReadOnlyList<ExpectedMustHave> actual)
    {
        var used = new bool[actual.Count];
        var found = 0;
        var correct = 0;
        var missed = new List<ExpectedMustHave>();

        foreach (var wanted in expected)
        {
            var wantedQuote = Normalize(wanted.Quote);
            var matched = false;
            for (var i = 0; i < actual.Count; i++)
            {
                if (used[i] || actual[i].Kind != wanted.Kind || !Overlaps(wantedQuote, Normalize(actual[i].Quote)))
                    continue;

                used[i] = true;
                matched = true;
                found++;
                if (wanted.Status is null || actual[i].Status == wanted.Status)
                    correct++;
                break;
            }

            if (!matched)
                missed.Add(wanted);
        }

        var extra = actual.Where((_, i) => !used[i]).ToList();
        return new MustHaveMatch(found, correct, missed, extra);
    }

    public static MustHaveScore Aggregate(IEnumerable<MustHaveScore> scores) =>
        scores.Aggregate(
            new MustHaveScore(0, 0, 0, 0),
            (sum, s) => new MustHaveScore(
                sum.Expected + s.Expected,
                sum.Found + s.Found,
                sum.Correct + s.Correct,
                sum.FalsePositives + s.FalsePositives));

    private static bool Overlaps(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));

    private static string Normalize(string text) =>
        Whitespace().Replace(text.ToLowerInvariant(), " ").Trim(' ', '.', ',', ';', ':');

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
