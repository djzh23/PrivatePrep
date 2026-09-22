using PrivatePrep.Services.Requirements;

namespace PrivatePrep.Tests.Services;

public class MustHaveJsonParserTests
{
    [Fact]
    public void TryParse_ValidJson_ReturnsAllItems()
    {
        const string raw = """
            {
              "must_haves": [
                { "kind": "abschluss", "posting_quote": "Abgeschlossene Ausbildung", "status": "met", "cv_quote": "Ausbildung abgeschlossen" },
                { "kind": "sprache", "posting_quote": "verhandlungssicheres Englisch", "status": "notMet" },
                { "kind": "arbeitszeit", "posting_quote": "Vollzeit", "status": "unclear" }
              ]
            }
            """;

        var ok = MustHaveJsonParser.TryParse(raw, out var proposals);

        Assert.True(ok);
        Assert.Equal(3, proposals.Count);
        Assert.Equal(RequirementKind.Abschluss, proposals[0].Kind);
        Assert.Equal(MustHaveStatus.Met, proposals[0].Status);
        Assert.Equal("Ausbildung abgeschlossen", proposals[0].CvQuote);
        Assert.Equal(MustHaveStatus.NotMet, proposals[1].Status);
        Assert.Null(proposals[1].CvQuote);
    }

    [Fact]
    public void TryParse_IgnoresProseAroundTheJsonObject()
    {
        const string raw = """
            Here is the analysis:
            { "must_haves": [ { "kind": "fuehrerschein", "posting_quote": "Führerschein Klasse B", "status": "unclear" } ] }
            Let me know if you need more.
            """;

        var ok = MustHaveJsonParser.TryParse(raw, out var proposals);

        Assert.True(ok);
        Assert.Single(proposals);
    }

    [Fact]
    public void TryParse_UnknownKind_SkipsOnlyThatItem()
    {
        const string raw = """
            {
              "must_haves": [
                { "kind": "sternzeichen", "posting_quote": "Löwe bevorzugt", "status": "unclear" },
                { "kind": "schicht", "posting_quote": "Schichtdienst", "status": "met", "cv_quote": "Schichtdienst geleistet" }
              ]
            }
            """;

        var ok = MustHaveJsonParser.TryParse(raw, out var proposals);

        Assert.True(ok);
        var kept = Assert.Single(proposals);
        Assert.Equal(RequirementKind.Schicht, kept.Kind);
    }

    [Fact]
    public void TryParse_UnknownStatus_SkipsOnlyThatItem()
    {
        const string raw = """
            {
              "must_haves": [
                { "kind": "sprache", "posting_quote": "Englisch", "status": "maybe" },
                { "kind": "sprache", "posting_quote": "Deutsch", "status": "met", "cv_quote": "Muttersprache Deutsch" }
              ]
            }
            """;

        var ok = MustHaveJsonParser.TryParse(raw, out var proposals);

        Assert.True(ok);
        Assert.Single(proposals);
    }

    [Fact]
    public void TryParse_MissingPostingQuote_SkipsThatItem()
    {
        const string raw = """{ "must_haves": [ { "kind": "sprache", "status": "unclear" } ] }""";

        var ok = MustHaveJsonParser.TryParse(raw, out var proposals);

        Assert.True(ok);
        Assert.Empty(proposals);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ this is not valid json")]
    public void TryParse_NoRecognisableJson_ReturnsFalse(string raw)
    {
        var ok = MustHaveJsonParser.TryParse(raw, out var proposals);

        Assert.False(ok);
        Assert.Empty(proposals);
    }

    [Fact]
    public void TryParse_EmptyMustHavesList_ReturnsTrueWithNoItems()
    {
        var ok = MustHaveJsonParser.TryParse("""{ "must_haves": [] }""", out var proposals);

        Assert.True(ok);
        Assert.Empty(proposals);
    }
}
