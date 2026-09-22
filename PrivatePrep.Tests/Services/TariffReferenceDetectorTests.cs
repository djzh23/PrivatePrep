using PrivatePrep.Services.Tariffs;
using PrivatePrep.Tests.GoldenSet;

namespace PrivatePrep.Tests.Services;

/// <summary>Expectations come from the golden set's six tariff-bearing postings (spec 001, step 5).</summary>
public class TariffReferenceDetectorTests
{
    private readonly TariffReferenceDetector _sut = new();

    private static string Jd(string id) => GoldenSetLoader.LoadAll().Single(c => c.Id == id).JobDescription;

    [Fact]
    public void Detect_GroupThenAgreement_Bildung01() // "Entgeltgruppe E 11 TV-L"
    {
        var result = _sut.Detect(Jd("bildung-01"));

        Assert.Equal(new TariffReference(TariffAgreement.TVL, "E11"), result);
    }

    [Fact]
    public void Detect_GroupThenTwoWordAgreement_EdgeBildung01() // "Entgeltgruppe S 8a TVöD SuE"
    {
        var result = _sut.Detect(Jd("edge-bildung-01"));

        Assert.Equal(new TariffReference(TariffAgreement.TVoeD, "S8a"), result);
    }

    [Fact]
    public void Detect_BareDigitGroup_EdgeVerwaltung01() // "Entgeltgruppe 6 TVöD"
    {
        var result = _sut.Detect(Jd("edge-verwaltung-01"));

        Assert.Equal(new TariffReference(TariffAgreement.TVoeD, "6"), result);
    }

    [Fact]
    public void Detect_AgreementBeforeGroupInParentheses_Pflege01() // "TVöD-P (Entgeltgruppe P 8)"
    {
        var result = _sut.Detect(Jd("pflege-01"));

        Assert.Equal(new TariffReference(TariffAgreement.TVoeD, "P8"), result);
    }

    [Fact]
    public void Detect_UnrelatedEarlierMentionIsIgnored_Verwaltung01()
    {
        // The posting says "sichere Kenntnisse im TV-L" earlier and only later cites the real Entgeltgruppe.
        var result = _sut.Detect(Jd("verwaltung-01"));

        Assert.Equal(new TariffReference(TariffAgreement.TVL, "9a"), result);
    }

    [Fact]
    public void Detect_GroupInParenthesesRightAfterTitle_EdgeBildung02() // "(Entgeltgruppe 13 TV-L)"
    {
        var result = _sut.Detect(Jd("edge-bildung-02"));

        Assert.Equal(new TariffReference(TariffAgreement.TVL, "13"), result);
    }

    [Fact]
    public void Detect_NoTariffMentionAtAll_ReturnsNull()
    {
        var result = _sut.Detect("Wir suchen eine Bürokraft. Bezahlung erfolgt übertariflich, nach Vereinbarung.");

        Assert.Null(result);
    }

    [Fact]
    public void Detect_AgreementMentionedWithoutEntgeltgruppe_ReturnsNull()
    {
        // A lone "TV-L" mention must never be mistaken for a tariff citation on its own.
        var result = _sut.Detect("Wir orientieren uns am TV-L, ohne feste Eingruppierung.");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_NoPosting_ReturnsNull(string? posting)
    {
        Assert.Null(_sut.Detect(posting));
    }
}
