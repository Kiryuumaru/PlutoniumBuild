using Xunit;

namespace Plutonium.Core.Tests;

/// <summary>
/// Smoke tests proving the Core test harness runs against landed Phase-1 code.
/// </summary>
public sealed class ContractNamingTests
{
    /// <summary>
    /// Verifies the per-job condition output naming helper returns the expected value.
    /// </summary>
    [Fact]
    public void ConditionOutputName_FormatsExpectedValue()
    {
        Assert.Equal("BUILD_CONDITION", PlutoniumPipelineContract.ConditionOutputName("build"));
    }
}
