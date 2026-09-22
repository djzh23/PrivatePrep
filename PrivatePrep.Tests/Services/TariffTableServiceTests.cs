using PrivatePrep.Services.Tariffs;

namespace PrivatePrep.Tests.Services;

public class TariffTableServiceTests
{
    private readonly TariffTableService _sut = new();

    [Fact]
    public void Lookup_BareDigitGroup_ResolvesToTheGeneralScale()
    {
        var result = _sut.Lookup(TariffAgreement.TVoeD, "6", new DateOnly(2026, 9, 22));

        Assert.NotNull(result);
        Assert.Equal("E6", result!.Group);
        Assert.Equal(6, result.MonthlyGrossPerStep.Count);
        Assert.Equal(3240.30m, result.MonthlyGrossPerStep[0]);
    }

    [Fact]
    public void Lookup_GroupWithLetterPrefixAndSuffix_TVL()
    {
        var result = _sut.Lookup(TariffAgreement.TVL, "9a", new DateOnly(2026, 9, 22));

        Assert.NotNull(result);
        Assert.Equal("E9a", result!.Group);
        Assert.Equal(4035.07m, result.MonthlyGrossPerStep[3]);
    }

    [Fact]
    public void Lookup_GroupOnlyInASubTableThatIsNotSeeded_ReturnsNullRatherThanAGuess()
    {
        // "S8a" (TVöD-SuE) and "P8" (TVöD-P) are not part of the seeded general-scale tables.
        Assert.Null(_sut.Lookup(TariffAgreement.TVoeD, "S8a", new DateOnly(2026, 9, 22)));
        Assert.Null(_sut.Lookup(TariffAgreement.TVoeD, "P8", new DateOnly(2026, 9, 22)));
    }

    [Fact]
    public void Lookup_UnknownGroup_ReturnsNull()
    {
        Assert.Null(_sut.Lookup(TariffAgreement.TVoeD, "E99", new DateOnly(2026, 9, 22)));
    }

    [Fact]
    public void Lookup_WithinTwelveMonthsOfStand_IsNotStale()
    {
        var result = _sut.Lookup(TariffAgreement.TVoeD, "E6", new DateOnly(2026, 9, 22));

        Assert.False(result!.IsStale);
    }

    [Fact]
    public void Lookup_MoreThanTwelveMonthsAfterStand_IsStale()
    {
        var result = _sut.Lookup(TariffAgreement.TVoeD, "E6", new DateOnly(2028, 1, 1));

        Assert.True(result!.IsStale);
    }

    [Fact]
    public void Lookup_ExposesSourceAndValidityForCitation()
    {
        var result = _sut.Lookup(TariffAgreement.TVL, "E1", new DateOnly(2026, 9, 22));

        Assert.Equal(new DateOnly(2026, 4, 1), result!.ValidFrom);
        Assert.Equal(new DateOnly(2027, 2, 28), result.ValidUntil);
        Assert.StartsWith("https://", result.Source);
    }
}

public class TariffTableCatalogTests
{
    private readonly TariffTableCatalog _catalog = TariffTableCatalog.LoadEmbedded();

    [Fact]
    public void LoadEmbedded_LoadsBothAgreements()
    {
        Assert.True(_catalog.TryGet(TariffAgreement.TVoeD, out _));
        Assert.True(_catalog.TryGet(TariffAgreement.TVL, out _));
    }

    [Theory]
    [InlineData(TariffAgreement.TVoeD)]
    [InlineData(TariffAgreement.TVL)]
    public void EveryGroup_HasNonDecreasingStepsAndAPlausibleRange(TariffAgreement agreement)
    {
        // Catches transcription mistakes: within one group, a later Erfahrungsstufe never pays less than an
        // earlier one, and every real monthly gross figure in these tables falls in a sane band.
        Assert.True(_catalog.TryGet(agreement, out var table));

        foreach (var (group, steps) in table.Groups)
        {
            decimal? previous = null;
            foreach (var step in steps)
            {
                if (step is null)
                    continue;

                Assert.InRange(step.Value, 2000m, 10000m);
                if (previous is not null)
                    Assert.True(step >= previous, $"{group}: a later step pays less than an earlier one ({previous} -> {step}).");
                previous = step;
            }
        }
    }

    [Theory]
    [InlineData(TariffAgreement.TVoeD)]
    [InlineData(TariffAgreement.TVL)]
    public void EveryTable_HasAValidityWindowAndASource(TariffAgreement agreement)
    {
        Assert.True(_catalog.TryGet(agreement, out var table));

        Assert.True(table.ValidFrom < table.ValidUntil);
        Assert.StartsWith("https://", table.Source);
        Assert.NotEmpty(table.Groups);
    }
}
