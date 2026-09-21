using System.Text.RegularExpressions;

namespace PrivatePrep.Tests.GoldenSet;

public sealed record MustHaveScore(int Expected, int Found, int Correct, int FalsePositives)
{
    /// <summary>Share of expected requirements that were found AND assessed correctly (AC-1: at least 0.95).</summary>
    public double Accuracy => Expected == 0 ? 1.0 : (double)Correct / Expected;

    /// <summary>Share of expected requirements that were found at all, whatever their status. Isolates extraction from the CV comparison.</summary>
    public double Recall => Expected == 0 ? 1.0 : (double)Found / Expected;
}

/// <summary>Compares the requirements an extractor found with the golden expectations.</summary>
public static partial class MustHaveScorer
{
    public static MustHaveScore Score(IReadOnlyList<ExpectedMustHave> expected, IReadOnlyList<ExpectedMustHave> actual)
    {
        var used = new bool[actual.Count];
        var found = 0;
        var correct = 0;

        foreach (var wanted in expected)
        {
            var wantedQuote = Normalize(wanted.Quote);
            for (var i = 0; i < actual.Count; i++)
            {
                if (used[i] || actual[i].Kind != wanted.Kind || !Overlaps(wantedQuote, Normalize(actual[i].Quote)))
                    continue;

                used[i] = true;
                found++;
                if (wanted.Status is null || actual[i].Status == wanted.Status)
                    correct++;
                break;
            }
        }

        return new MustHaveScore(expected.Count, found, correct, used.Count(u => !u));
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
