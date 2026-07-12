using JTalk.Config;
using Xunit;

namespace JTalk.Tests;

public sealed class ApiKeysTests
{
    [Fact]
    public void ResolveReturnsFirstNonEmptyCandidate() =>
        Assert.Equal("key", ApiKeys.Resolve(null, "", "   ", "key", "later"));

    [Fact]
    public void ResolveReturnsNullWhenAllCandidatesAreEmpty() =>
        Assert.Null(ApiKeys.Resolve(null, "", "  "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnvReturnsNullForMissingName(string? name) =>
        Assert.Null(ApiKeys.Env(name));

    [Fact]
    public void EnvReadsTheNamedVariable()
    {
        const string variableName = "JTALK_TESTS_API_KEY_PROBE";
        Environment.SetEnvironmentVariable(variableName, "probe-value");
        try
        {
            Assert.Equal("probe-value", ApiKeys.Env(variableName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }
}
