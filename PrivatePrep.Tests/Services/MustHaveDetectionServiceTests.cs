using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PrivatePrep.Services.Agent;
using PrivatePrep.Services.Requirements;

namespace PrivatePrep.Tests.Services;

public class MustHaveDetectionServiceTests
{
    private readonly Mock<ILlmRouter> _llm = new();
    private readonly Mock<IRequirementsExtractor> _fallback = new();

    private const string Posting = "Ihr Profil: Führerschein Klasse B. Verhandlungssicheres Englisch.";
    private const string Cv = "Führerschein Klasse B seit 2018.";

    private MustHaveDetectionService CreateSut() =>
        new(_llm.Object, new MustHaveVerifier(), _fallback.Object, NullLogger<MustHaveDetectionService>.Instance);

    private static LlmResponse Response(string json, string model = "openai/gpt-oss-120b") => new(json, model, 100, 80);

    [Fact]
    public async Task DetectAsync_ValidResponse_ReturnsVerifiedItemsAndTheModelUsed()
    {
        const string json = """
            { "must_haves": [
              { "kind": "fuehrerschein", "posting_quote": "Führerschein Klasse B", "status": "met", "cv_quote": "Führerschein Klasse B seit 2018" }
            ] }
            """;
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(json));

        var result = await CreateSut().DetectAsync(Posting, Cv, CancellationToken.None);

        var item = Assert.Single(result.Verified);
        Assert.Equal(MustHaveStatus.Met, item.Status);
        Assert.Equal("openai/gpt-oss-120b", result.ModelUsed);
        Assert.False(result.UsedFallback);
        _fallback.Verify(f => f.Extract(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DetectAsync_ModelInventsAQuote_RejectsThatItemButKeepsTheRealOne()
    {
        const string json = """
            { "must_haves": [
              { "kind": "fuehrerschein", "posting_quote": "Führerschein Klasse B", "status": "unclear" },
              { "kind": "zertifikat", "posting_quote": "Meisterbrief erforderlich", "status": "notMet" }
            ] }
            """;
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(json));

        var result = await CreateSut().DetectAsync(Posting, Cv, CancellationToken.None);

        Assert.Single(result.Verified);
        Assert.Single(result.Rejected);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task DetectAsync_InvalidJsonOnFirstTry_RetriesOnceThenSucceeds()
    {
        const string badJson = "not json";
        const string goodJson = """{ "must_haves": [ { "kind": "sprache", "posting_quote": "Verhandlungssicheres Englisch", "status": "unclear" } ] }""";
        _llm.SetupSequence(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(badJson))
            .ReturnsAsync(Response(goodJson));

        var result = await CreateSut().DetectAsync(Posting, Cv, CancellationToken.None);

        Assert.Single(result.Verified);
        _llm.Verify(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DetectAsync_InvalidJsonTwice_FallsBackToTheRuleBasedExtractor()
    {
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("not json"));
        _fallback.Setup(f => f.Extract(Posting))
            .Returns([new ExtractedRequirement(RequirementKind.Fuehrerschein, "Führerschein Klasse B")]);

        var result = await CreateSut().DetectAsync(Posting, Cv, CancellationToken.None);

        var item = Assert.Single(result.Verified);
        Assert.Equal(MustHaveStatus.Unclear, item.Status);
        Assert.True(result.UsedFallback);
        Assert.Null(result.ModelUsed);
    }

    [Fact]
    public async Task DetectAsync_LlmThrows_FallsBackToTheRuleBasedExtractor()
    {
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AnalyzeException("llm_unavailable", "down"));
        _fallback.Setup(f => f.Extract(Posting))
            .Returns([new ExtractedRequirement(RequirementKind.Sprache, "Verhandlungssicheres Englisch")]);

        var result = await CreateSut().DetectAsync(Posting, Cv, CancellationToken.None);

        Assert.Single(result.Verified);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task DetectAsync_EveryProposalIsHallucinated_FallsBackInsteadOfReturningNothing()
    {
        const string json = """{ "must_haves": [ { "kind": "zertifikat", "posting_quote": "Meisterbrief erforderlich", "status": "notMet" } ] }""";
        _llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(json));
        _fallback.Setup(f => f.Extract(Posting))
            .Returns([new ExtractedRequirement(RequirementKind.Fuehrerschein, "Führerschein Klasse B")]);

        var result = await CreateSut().DetectAsync(Posting, Cv, CancellationToken.None);

        Assert.Single(result.Verified);
        Assert.True(result.UsedFallback);
        Assert.Equal(RequirementKind.Fuehrerschein, result.Verified[0].Kind);
    }
}
