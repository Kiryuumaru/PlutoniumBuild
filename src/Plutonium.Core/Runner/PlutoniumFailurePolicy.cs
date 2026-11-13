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
/// What the runner does with the remaining plan when a target fails.
/// </summary>
public enum PlutoniumFailurePolicy
{
    /// <summary>Stop the run at the first failure.</summary>
    StopOnFailure = 0,

    /// <summary>Continue with the remaining targets after recording the failure.</summary>
    ProceedAfterFailure = 1,

    /// <summary>Run only the targets assured to run after a failure, then stop.</summary>
    AssuredAfterFailure = 2,
}
