using Plutonium.Core;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Runner;

/// <summary>
/// Why a planned target did not execute its action.
/// </summary>
public enum PlutoniumSkipReason
{
    /// <summary>The target was excluded via a skip request.</summary>
    SkippedByRequest = 0,

    /// <summary>A static plan-time condition evaluated to false.</summary>
    StaticConditionFalse = 1,

    /// <summary>A dynamic run-time condition evaluated to false.</summary>
    DynamicConditionFalse = 2,

    /// <summary>A required target was missing from the plan.</summary>
    MissingRequirement = 3,

    /// <summary>An upstream dependency failed or was skipped.</summary>
    UpstreamNotSatisfied = 4,
}
