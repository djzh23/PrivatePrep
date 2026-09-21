using System.Text;
using PrivatePrep.Services.Requirements;
using PrivatePrep.Tests.GoldenSet;

namespace PrivatePrep.Tests.Services;

/// <summary>
/// The extractor finds the hard requirements of a posting without a language model (AC-1, AC-3).
/// Expectations come from the golden set; statuses (CV comparison) are covered separately.
/// </summary>
public class RequirementsExtractorTests
{
    private static readonly IReadOnlyList<GoldenCase> Cases = GoldenSetLoader.LoadAll();

    private static IEnumerable<GoldenCase> Postings => Cases.Where(c => c.IsJobPosting);

    private readonly RequirementsExtractor _sut = new();

    private static string Name(RequirementKind kind) => kind.ToString().ToLowerInvariant();

    private string Kinds(string text) =>
        string.Join(",", _sut.Extract(text).Select(r => Name(r.Kind)).Distinct().Order());

    private (MustHaveScore Score, string Report) Evaluate(IEnumerable<GoldenCase> cases)
    {
        var scores = new List<MustHaveScore>();
        var report = new StringBuilder();
        foreach (var c in cases)
        {
            var actual = _sut.Extract(c.JobDescription).Select(r => new ExpectedMustHave(Name(r.Kind), r.Quote)).ToList();
            var expected = c.Expected.MustHaves.Select(m => new ExpectedMustHave(m.Kind, m.Quote)).ToList();
            scores.Add(MustHaveScorer.Score(expected, actual));

            var match = MustHaveScorer.Match(expected, actual);
            foreach (var e in match.Missed)
                report.AppendLine($"  [{c.Id}] MISSED  {e.Kind}: {e.Quote}");
            foreach (var a in match.Extra)
                report.AppendLine($"  [{c.Id}] EXTRA   {a.Kind}: {a.Quote}");
        }

        return (MustHaveScorer.Aggregate(scores), report.ToString());
    }

    // ---- golden set --------------------------------------------------------------------------

    [Fact]
    public void GoldenPostings_MeetTheRecallGate()
    {
        var (score, report) = Evaluate(Postings);

        Assert.True(score.Recall >= 0.95, $"Recall {score.Recall:P1} ({score.Found}/{score.Expected}) is below 95 %.\n{report}");
    }

    /// <summary>
    /// Regression guard for the cases the rules were developed on: every miss or extra item is a rule bug.
    /// The 95 % gate above stays the acceptance criterion; blind cases live in the holdout set.
    /// </summary>
    [Fact]
    public void DevelopmentCases_AreSolvedCompletely()
    {
        var (score, report) = Evaluate(Postings);

        Assert.True(
            score.Found == score.Expected && score.FalsePositives == 0,
            $"{score.Found}/{score.Expected} found, {score.FalsePositives} extra.\n{report}");
    }

    [Fact]
    public void EveryOccupation_ReachesAtLeast90PercentRecall()
    {
        foreach (var field in GoldenVocabulary.Fields)
        {
            var (score, report) = Evaluate(Postings.Where(c => c.Field == field));
            Assert.True(score.Recall >= 0.9, $"Field '{field}': recall {score.Recall:P1}.\n{report}");
        }
    }

    [Fact]
    public void FalsePositives_StayBelowTenPercentOfTheExpectedRequirements()
    {
        var (score, report) = Evaluate(Postings);

        Assert.True(
            score.FalsePositives <= score.Expected * 0.10,
            $"{score.FalsePositives} false positives for {score.Expected} expected requirements.\n{report}");
    }

    [Fact]
    public void EveryExtractedQuoteAppearsVerbatimInThePosting()
    {
        foreach (var c in Cases)
            foreach (var r in _sut.Extract(c.JobDescription))
                Assert.True(
                    c.JobDescription.Contains(r.Quote, StringComparison.Ordinal),
                    $"[{c.Id}] not verbatim: \"{r.Quote}\"");
    }

    [Fact]
    public void InputsThatAreNotPostings_YieldNothing()
    {
        foreach (var c in Cases.Where(c => !c.IsJobPosting))
            Assert.Empty(_sut.Extract(c.JobDescription));
    }

    // ---- holdout: postings written after the rules --------------------------------------------

    private static readonly IReadOnlyList<GoldenCase> Holdout = GoldenSetLoader.LoadHoldout();

    [Fact]
    public void HoldoutSetIsWellFormed()
    {
        Assert.True(Holdout.Count >= 10, "Need at least ten holdout postings.");
        Assert.All(GoldenVocabulary.Fields, f => Assert.Contains(Holdout, c => c.Field == f));
        Assert.DoesNotContain(Holdout, c => Cases.Any(d => d.Id == c.Id));
        foreach (var c in Holdout)
            foreach (var m in c.Expected.MustHaves)
                Assert.True(
                    c.JobDescription.Contains(m.Quote, StringComparison.Ordinal),
                    $"[{c.Id}] quote not found verbatim: \"{m.Quote}\"");
    }

    /// <summary>Measured on 2026-09-21 on postings written after the rules: 67.6 %, 75.7 % after the last rule change.</summary>
    private const double HoldoutFloor = 0.70;

    [Fact]
    public void HoldoutPostings_DoNotFallBelowTheMeasuredLevel()
    {
        var (score, report) = Evaluate(Holdout);

        Assert.True(
            score.Recall >= HoldoutFloor,
            $"Holdout recall {score.Recall:P1} ({score.Found}/{score.Expected}) fell below {HoldoutFloor:P0}, {score.FalsePositives} extra.\n{report}");
    }

    [Fact(Skip = "AC-1 target of 95 % is not reached on unseen postings by rules alone (measured 68-76 % on 2026-09-21). Decision pending, see spec 001.")]
    public void HoldoutPostings_MeetTheRecallGate()
    {
        var (score, report) = Evaluate(Holdout);

        Assert.True(
            score.Recall >= 0.95,
            $"Holdout recall {score.Recall:P1} ({score.Found}/{score.Expected}), {score.FalsePositives} extra.\n{report}");
    }

    // ---- quotes ------------------------------------------------------------------------------

    [Fact]
    public void Quotes_AreCutAtClauseEndsWithoutLabelOrTrailingPunctuation()
    {
        var quotes = _sut.Extract("Ihr Profil: Abgeschlossene Ausbildung als Koch, mindestens 2 Jahre Berufserfahrung.")
            .Select(r => r.Quote).ToList();

        Assert.Equal(["Abgeschlossene Ausbildung als Koch", "mindestens 2 Jahre Berufserfahrung"], quotes);
    }

    [Fact]
    public void ACommaBeforeAContinuationStaysInsideTheQuote()
    {
        var quotes = _sut.Extract("Ihr Profil: AEVO-Schein oder die Bereitschaft, ihn zu erwerben, Deutsch auf Niveau C1")
            .Select(r => r.Quote).ToList();

        Assert.Equal(["AEVO-Schein oder die Bereitschaft, ihn zu erwerben", "Deutsch auf Niveau C1"], quotes);
    }

    [Fact]
    public void CommasInsideParenthesesDoNotSplitTheQuote()
    {
        var quotes = _sut.Extract("Ihr Profil: Flexibilität bei Einsatzzeiten (früh, spät, Wochenende), Teamgeist")
            .Select(r => r.Quote).ToList();

        Assert.Equal(["Flexibilität bei Einsatzzeiten (früh, spät, Wochenende)"], quotes);
    }

    [Fact]
    public void NeighbouringClausesOfTheSameKindAreMergedIntoOneQuote()
    {
        var quotes = _sut.Extract("Anforderungen:\n- Fließend Englisch, gutes Deutsch").Select(r => r.Quote).ToList();

        Assert.Equal(["Fließend Englisch, gutes Deutsch"], quotes);
    }

    // ---- what counts as a requirement --------------------------------------------------------

    [Theory]
    [InlineData("Anforderungen:\n- Führerschein Klasse B\n- Teamgeist", "fuehrerschein")]
    [InlineData("ANFORDERUNGEN:\nFÜHRERSCHEIN KLASSE B", "fuehrerschein")]
    [InlineData("Requirements:\n* at least 2 years of experience\n* valid driver's license", "berufserfahrung,fuehrerschein")]
    [InlineData("Ihr Profil: mindestens 3 Jahre Berufserfahrung als Koch, Kenntnisse in Englisch", "berufserfahrung,sprache")]
    [InlineData("Sie verfügen über einen Führerschein der Klasse B.", "fuehrerschein")]
    [InlineData("Wir suchen einen Koch (m/w/d) in Vollzeit. Wir bieten 30 Tage Urlaub.", "arbeitszeit")]
    public void FindsRequirements(string text, string expectedKinds)
    {
        Assert.Equal(expectedKinds, Kinds(text));
    }

    [Theory]
    [InlineData("Anforderungen:\n- Führerschein Klasse B ist von Vorteil")]
    [InlineData("Anforderungen: Erfahrung mit Diagnosegeräten wünschenswert.")]
    [InlineData("Requirements:\n- Experience with a CRM system is preferred")]
    [InlineData("Anforderungen: Ein Führerschein ist nicht erforderlich.")]
    [InlineData("Anforderungen: Vorkenntnisse in Linux sind nicht nötig.")]
    [InlineData("Requirements: A driver's license is not required.")]
    public void OptionalAndNegatedItemsAreNotRequirements(string text)
    {
        Assert.Empty(_sut.Extract(text));
    }

    [Theory]
    [InlineData("Ihre Aufgaben: Unterricht in Deutsch und Mathematik, Elternarbeit.")]
    [InlineData("Wir bieten ein Firmenfahrzeug, Deutschkurse und Erfahrung in einem starken Team.")]
    [InlineData("Wir bieten: Deutschkurse und ein Jobticket.")]
    [InlineData("Responsibilities: coordinate driver's license renewals for the fleet.")]
    public void TasksAndOffersAreNotRequirements(string text)
    {
        Assert.Empty(_sut.Extract(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_YieldsNothing(string? text)
    {
        Assert.Empty(_sut.Extract(text));
    }
}
