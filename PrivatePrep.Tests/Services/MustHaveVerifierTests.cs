using PrivatePrep.Services.Requirements;

namespace PrivatePrep.Tests.Services;

/// <summary>
/// AC-1 decision A: the model proposes each must-have (kind, posting quote, status vs. CV), code verifies.
/// This is the code half — it never judges whether a requirement is met, only whether the model's quotes
/// are real. Step 3a's <see cref="RequirementsExtractor"/> stays as a rule-based fallback/cross-check.
/// </summary>
public class MustHaveVerifierTests
{
    private const string Posting = """
        Ihr Profil:
        - Abgeschlossene Ausbildung als Elektroniker für Energie- und Gebäudetechnik
        - Führerschein Klasse B
        """;

    private const string Cv = """
        Ausbildung
        Ausbildung zum Elektroniker für Energie- und Gebäudetechnik, IHK Köln.

        Führerschein
        Führerschein Klasse B seit 2018.
        """;

    private readonly MustHaveVerifier _sut = new();

    [Fact]
    public void Verify_PostingQuoteFoundVerbatim_KeepsTheRequirement()
    {
        var proposal = new ProposedMustHave(
            RequirementKind.Abschluss,
            "Abgeschlossene Ausbildung als Elektroniker für Energie- und Gebäudetechnik",
            MustHaveStatus.Unclear);

        var result = _sut.Verify([proposal], Posting, Cv);

        var kept = Assert.Single(result.Verified);
        Assert.Equal(RequirementKind.Abschluss, kept.Kind);
        Assert.Equal(MustHaveStatus.Unclear, kept.Status);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Verify_PostingQuoteDiffersOnlyInWhitespaceAndCase_StillMatches()
    {
        var proposal = new ProposedMustHave(
            RequirementKind.Fuehrerschein,
            "FÜHRERSCHEIN   klasse b",
            MustHaveStatus.Unclear);

        var result = _sut.Verify([proposal], Posting, Cv);

        Assert.Single(result.Verified);
    }

    [Fact]
    public void Verify_PostingQuoteNotInPosting_IsRejectedNotShown()
    {
        // The model invented a requirement the posting never states (AC-2: no invented facts).
        var proposal = new ProposedMustHave(RequirementKind.Zertifikat, "Meisterbrief erforderlich", MustHaveStatus.NotMet);

        var result = _sut.Verify([proposal], Posting, Cv);

        Assert.Empty(result.Verified);
        Assert.Same(proposal, Assert.Single(result.Rejected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Verify_EmptyPostingQuote_IsRejected(string? quote)
    {
        var proposal = new ProposedMustHave(RequirementKind.Sprache, quote!, MustHaveStatus.Unclear);

        var result = _sut.Verify([proposal], Posting, Cv);

        Assert.Empty(result.Verified);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void Verify_MetWithCvQuoteFoundInCv_KeepsMetStatus()
    {
        var proposal = new ProposedMustHave(
            RequirementKind.Fuehrerschein,
            "Führerschein Klasse B",
            MustHaveStatus.Met,
            CvQuote: "Führerschein Klasse B seit 2018");

        var result = _sut.Verify([proposal], Posting, Cv);

        Assert.Equal(MustHaveStatus.Met, Assert.Single(result.Verified).Status);
    }

    [Fact]
    public void Verify_MetWithoutCvQuote_IsDowngradedToUnclearButNotRejected()
    {
        // Never invent a match: an unproven "met" claim is not discarded, it is softened. The requirement
        // itself is real (posting quote verified) and still belongs in the report.
        var proposal = new ProposedMustHave(RequirementKind.Fuehrerschein, "Führerschein Klasse B", MustHaveStatus.Met);

        var result = _sut.Verify([proposal], Posting, Cv);

        var kept = Assert.Single(result.Verified);
        Assert.Equal(MustHaveStatus.Unclear, kept.Status);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Verify_MetWithCvQuoteNotActuallyInCv_IsDowngradedToUnclear()
    {
        var proposal = new ProposedMustHave(
            RequirementKind.Fuehrerschein,
            "Führerschein Klasse B",
            MustHaveStatus.Met,
            CvQuote: "Führerschein Klasse C, seit 2020");

        var result = _sut.Verify([proposal], Posting, Cv);

        Assert.Equal(MustHaveStatus.Unclear, Assert.Single(result.Verified).Status);
    }

    [Fact]
    public void Verify_NotMet_NeedsNoCvEvidence()
    {
        // An absence can't be "quoted" from a CV, so NotMet is trusted without a CvQuote.
        var proposal = new ProposedMustHave(RequirementKind.Fuehrerschein, "Führerschein Klasse B", MustHaveStatus.NotMet);

        var result = _sut.Verify([proposal], Posting, Cv);

        Assert.Equal(MustHaveStatus.NotMet, Assert.Single(result.Verified).Status);
    }

    [Fact]
    public void Verify_Unclear_PassesThroughUnchanged()
    {
        var proposal = new ProposedMustHave(RequirementKind.Fuehrerschein, "Führerschein Klasse B", MustHaveStatus.Unclear);

        var result = _sut.Verify([proposal], Posting, Cv);

        Assert.Equal(MustHaveStatus.Unclear, Assert.Single(result.Verified).Status);
    }

    [Fact]
    public void Verify_MixOfValidAndInventedProposals_SplitsThemCorrectly()
    {
        var real = new ProposedMustHave(RequirementKind.Abschluss, "Abgeschlossene Ausbildung als Elektroniker für Energie- und Gebäudetechnik", MustHaveStatus.Met, "Ausbildung zum Elektroniker für Energie- und Gebäudetechnik");
        var invented = new ProposedMustHave(RequirementKind.Sprache, "Verhandlungssicheres Französisch", MustHaveStatus.NotMet);

        var result = _sut.Verify([real, invented], Posting, Cv);

        Assert.Single(result.Verified);
        Assert.Single(result.Rejected);
        Assert.Equal(RequirementKind.Abschluss, result.Verified[0].Kind);
    }

    [Fact]
    public void Verify_NoProposals_ReturnsEmptyResult()
    {
        var result = _sut.Verify([], Posting, Cv);

        Assert.Empty(result.Verified);
        Assert.Empty(result.Rejected);
    }
}
