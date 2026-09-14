using Xunit;

namespace Plutonium.Helpers.Tests;

/// <summary>
/// Smoke tests proving the Helpers test harness runs against landed Phase-1 code.
/// </summary>
public sealed class AppIdTests
{
    /// <summary>
    /// Verifies a valid application identifier round-trips through its validated value.
    /// </summary>
    [Fact]
    public void ValidValue_RoundTrips()
    {
        Assert.Equal("my-app", new PlutoniumAppId("my-app").Value);
    }
}
