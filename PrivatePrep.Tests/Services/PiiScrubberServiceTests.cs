using PrivatePrep.Services.Privacy;

namespace PrivatePrep.Tests.Services;

public class PiiScrubberServiceTests
{
    private readonly PiiScrubberService _sut = new();

    [Fact]
    public void Scrub_ReplacesEmail()
    {
        var result = _sut.ScrubBestEffort("Kontakt: max.mustermann@example.com bitte mailen.");
        Assert.Contains(PiiScrubberService.EmailPlaceholder, result);
        Assert.DoesNotContain("max.mustermann@example.com", result);
        Assert.Contains("Kontakt:", result);
    }

    [Fact]
    public void Scrub_ReplacesGermanMobileAndLandline()
    {
        var result = _sut.ScrubBestEffort("Tel +49 40 1234567 oder 0171 2345678.");
        Assert.Contains(PiiScrubberService.PhonePlaceholder, result);
        Assert.DoesNotContain("1234567", result);
        Assert.DoesNotContain("2345678", result);
    }

    [Fact]
    public void Scrub_ReplacesHttpsUrl()
    {
        var result = _sut.ScrubBestEffort("Portfolio: https://example.com/cv.pdf Ende.");
        Assert.Contains(PiiScrubberService.UrlPlaceholder, result);
        Assert.DoesNotContain("example.com", result);
    }

    [Fact]
    public void Scrub_LeavesNamesAndJobFacts()
    {
        var input = "Max Mustermann, Pflegefachkraft 2020-2024 in Hamburg, Skills: Wundversorgung.";
        var result = _sut.ScrubBestEffort(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Scrub_Empty_ReturnsEmpty()
    {
        Assert.Equal("", _sut.ScrubBestEffort(""));
    }
}
