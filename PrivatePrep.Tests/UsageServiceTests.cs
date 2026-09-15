using PrivatePrep.Services.Tracking;

namespace PrivatePrep.Tests;

public class UsageServiceTests
{
    [Theory]
    [InlineData("anonymous", 2)]
    [InlineData("free", 3)]
    [InlineData("premium", int.MaxValue)]
    [InlineData("pro", int.MaxValue)]
    public void GetDailyLimit_MatchesV1Blueprint(string plan, int expected) =>
        Assert.Equal(expected, UsageService.GetDailyLimit(plan));
}
