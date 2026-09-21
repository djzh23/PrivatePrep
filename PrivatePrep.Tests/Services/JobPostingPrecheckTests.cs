using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Posting;
using PrivatePrep.Tests.GoldenSet;

namespace PrivatePrep.Tests.Services;

public class JobPostingPrecheckTests
{
    private static readonly IReadOnlyList<GoldenCase> Cases = GoldenSetLoader.LoadAll();

    private readonly JobPostingPrecheck _sut = new();

    private static string Repeat(string sentence, int times) => string.Concat(Enumerable.Repeat(sentence, times));

    private const string PostingSentence = "Wir suchen Verstärkung für unser Team in Vollzeit mit Bewerbung. ";

    // ---- is it a job posting? (AC-17) --------------------------------------------------------

    [Fact]
    public void EveryGoldenPostingIsRecognisedAsAPosting()
    {
        var missed = Cases.Where(c => c.IsJobPosting && !_sut.Check(c.JobDescription).IsJobPosting)
            .Select(c => c.Id).ToList();

        Assert.Empty(missed);
    }

    [Fact]
    public void GoldenInputsThatAreNotPostingsAreRejected()
    {
        var accepted = Cases.Where(c => !c.IsJobPosting && _sut.Check(c.JobDescription).IsJobPosting)
            .Select(c => c.Id).ToList();

        Assert.Empty(accepted);
    }

    [Fact]
    public void ACvPastedIntoThePostingFieldIsNotMistakenForAPosting()
    {
        var mistaken = Cases.Where(c => !string.IsNullOrWhiteSpace(c.CvText) && _sut.Check(c.CvText!).IsJobPosting)
            .Select(c => c.Id).ToList();

        Assert.Empty(mistaken);
    }

    [Fact]
    public void EveryGoldenPostingShowsAtLeastTheMinimumNumberOfSignals()
    {
        foreach (var c in Cases.Where(c => c.IsJobPosting))
            Assert.True(
                _sut.Check(c.JobDescription).Signals.Count >= JobPostingPrecheck.MinimumSignals,
                $"[{c.Id}] fewer than {JobPostingPrecheck.MinimumSignals} posting signals.");
    }

    [Fact]
    public void TwoSignalsAreNotEnough()
    {
        // hiring intent + application, nothing else
        var result = _sut.Check("Wir suchen jemanden für das Lager, ab sofort. Bewerbung bitte per Post an die Adresse unten, danke.");

        Assert.False(result.IsJobPosting);
        Assert.Equal(2, result.Signals.Count);
    }

    [Fact]
    public void ThreeSignalsAreEnough()
    {
        var result = _sut.Check("Wir suchen jemanden für das Lager in Vollzeit, ab sofort. Bewerbung bitte per Post an die Adresse unten, danke.");

        Assert.True(result.IsJobPosting);
        Assert.Equal(3, result.Signals.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void EmptyInput_IsNotAPostingAndDoesNotThrow(string? input)
    {
        var result = _sut.Check(input);

        Assert.False(result.IsJobPosting);
        Assert.Equal(PostingLanguage.Unknown, result.Language);
        Assert.False(result.Truncated);
        Assert.Equal("", result.Text);
        Assert.Equal(0, result.OriginalLength);
    }

    // ---- language ----------------------------------------------------------------------------

    [Fact]
    public void LanguageMatchesTheGoldenLabelForEveryCase()
    {
        var wrong = Cases
            .Select(c => (c.Id, Expected: c.Language == "en" ? PostingLanguage.English : PostingLanguage.German,
                Actual: _sut.Check(c.JobDescription).Language))
            .Where(x => x.Expected != x.Actual)
            .Select(x => $"{x.Id}: expected {x.Expected}, got {x.Actual}")
            .ToList();

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("Wir suchen eine Verstärkung für unser Team in München und bieten Ihnen einen sicheren Arbeitsplatz.", PostingLanguage.German)]
    [InlineData("We are looking for a team player to join our office in London and help us with the daily work.", PostingLanguage.English)]
    [InlineData("Nous recherchons un développeur pour rejoindre notre équipe à Paris et travailler sur des projets.", PostingLanguage.Unknown)]
    [InlineData("Hallo Welt", PostingLanguage.Unknown)]
    [InlineData("12345 67890 !!! ???", PostingLanguage.Unknown)]
    [InlineData("das ist der und the and to we", PostingLanguage.Unknown)]
    public void Language_IsDetectedFromCommonWords(string text, PostingLanguage expected)
    {
        Assert.Equal(expected, _sut.Check(text).Language);
    }

    // ---- truncation (AC-18) --------------------------------------------------------------------

    [Fact]
    public void ShortPosting_IsReturnedTrimmedAndNotTruncated()
    {
        var posting = Cases.First(c => c.IsJobPosting).JobDescription;

        var result = _sut.Check("  \n" + posting + "\n  ");

        Assert.False(result.Truncated);
        Assert.Equal(posting.Trim(), result.Text);
        Assert.Equal(posting.Trim().Length, result.OriginalLength);
    }

    [Fact]
    public void PostingExactlyAtTheLimit_IsNotTruncated()
    {
        var text = new string('x', AnalyzeService.MaxJobDescriptionLength);

        var result = _sut.Check(text);

        Assert.False(result.Truncated);
        Assert.Equal(AnalyzeService.MaxJobDescriptionLength, result.Text.Length);
    }

    [Fact]
    public void PostingOneCharacterOverTheLimit_IsTruncated()
    {
        var text = new string('x', AnalyzeService.MaxJobDescriptionLength + 1);

        var result = _sut.Check(text);

        Assert.True(result.Truncated);
        Assert.Equal(AnalyzeService.MaxJobDescriptionLength, result.Text.Length);
    }

    [Fact]
    public void LongPosting_IsCutAtAWordBoundaryAndReportsItsOriginalLength()
    {
        var input = Repeat(PostingSentence, 300).Trim();

        var result = _sut.Check(input);

        Assert.True(result.IsJobPosting);
        Assert.True(result.Truncated);
        Assert.Equal(input.Length, result.OriginalLength);
        Assert.True(result.Text.Length <= AnalyzeService.MaxJobDescriptionLength);
        Assert.True(result.Text.Length > AnalyzeService.MaxJobDescriptionLength * 0.9);
        Assert.StartsWith(result.Text, input, StringComparison.Ordinal);
        Assert.False(char.IsWhiteSpace(result.Text[^1]));
        Assert.True(char.IsWhiteSpace(input[result.Text.Length]), "The cut must fall between two words.");
    }

    [Fact]
    public void TruncationIsReportedEvenWhenTheInputIsNotAPosting()
    {
        var input = Repeat("Zutaten für vier Personen: Nudeln, Tomaten und Basilikum. ", 400);

        var result = _sut.Check(input);

        Assert.False(result.IsJobPosting);
        Assert.True(result.Truncated);
    }
}
