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
/// Aggregate result of a runner execution: per-target summaries plus totals.
/// </summary>
public sealed class PlutoniumRunResult
{
    /// <summary>Gets the per-target summaries in execution order.</summary>
    public IReadOnlyList<PlutoniumTargetSummary> Targets { get; }

    /// <summary>Gets whether every executed target succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the total wall-clock time of the run.</summary>
    public TimeSpan TotalDuration { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumRunResult"/> class.
    /// </summary>
    /// <param name="targets">Per-target summaries. Must not be null.</param>
    /// <param name="succeeded">Whether the run succeeded.</param>
    /// <param name="totalDuration">Total wall-clock time.</param>
    public PlutoniumRunResult(
        IReadOnlyList<PlutoniumTargetSummary> targets,
        bool succeeded,
        TimeSpan totalDuration)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets;
        Succeeded = succeeded;
        TotalDuration = totalDuration;
    }
}

