using PrivatePrep.Services.Profile;

namespace PrivatePrep.Tests.Services;

public class CvContentHasherTests
{
    [Fact]
    public void Sha256Hex_KnownVector_MatchesNist()
    {
        var hex = CvContentHasher.Sha256Hex("abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex);
    }

    [Fact]
    public void HexEquals_IgnoresCase()
    {
        var hex = CvContentHasher.Sha256Hex("cv-text");
        Assert.True(CvContentHasher.HexEquals(hex, hex.ToUpperInvariant()));
    }

    [Fact]
    public void HexEquals_DifferentHashes_AreNotEqual()
    {
        var a = CvContentHasher.Sha256Hex("one");
        var b = CvContentHasher.Sha256Hex("two");
        Assert.False(CvContentHasher.HexEquals(a, b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void IsSha256Hex_RejectsInvalid(string? value)
    {
        Assert.False(CvContentHasher.IsSha256Hex(value));
    }
}
