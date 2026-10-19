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
/// Per-target outcome recorded by the runner for the end-of-run summary.
/// </summary>
public enum PlutoniumTargetOutcome
{
    /// <summary>The target executed successfully.</summary>
    Succeeded = 0,

    /// <summary>The target did not execute.</summary>
    Skipped = 1,

    /// <summary>The target threw during execution.</summary>
    Failed = 2,
}

