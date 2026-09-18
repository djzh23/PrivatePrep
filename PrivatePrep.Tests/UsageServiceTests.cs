using PrivatePrep.Services.Tracking;

namespace PrivatePrep.Tests;

public class UsageServiceTests
{
    [Theory]
    [InlineData("anonymous", 1)]
    [InlineData("free", 1)]
    [InlineData("premium", 30)]
    [InlineData("unknown", 1)]
    public void GetDailyLimit_MatchesV1Blueprint(string plan, int expected) =>
        Assert.Equal(expected, UsageService.GetDailyLimit(plan));
}
