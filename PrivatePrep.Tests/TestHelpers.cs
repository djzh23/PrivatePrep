using Microsoft.Extensions.Configuration;

namespace PrivatePrep.Tests;

internal static class TestHelpers
{
    internal static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();
}
