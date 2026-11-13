using Xunit;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

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
