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
/// Timing and outcome for a single executed or skipped target.
/// </summary>
public sealed class PlutoniumTargetSummary
{
    /// <summary>Gets the target name.</summary>
    public string TargetName { get; }

    /// <summary>Gets the outcome.</summary>
    public PlutoniumTargetOutcome Outcome { get; }

    /// <summary>Gets how long the target took, including skip handling.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the skip reason when the outcome is skipped.</summary>
    public PlutoniumSkipReason? SkipReason { get; }

    /// <summary>Gets the failure when the outcome is failed.</summary>
    public Exception? Failure { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumTargetSummary"/> class.
    /// </summary>
    /// <param name="targetName">The target name. Must not be null or whitespace.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="duration">How long the target took.</param>
    /// <param name="skipReason">The skip reason, if skipped.</param>
    /// <param name="failure">The failure, if failed.</param>
    public PlutoniumTargetSummary(
        string targetName,
        PlutoniumTargetOutcome outcome,
        TimeSpan duration,
        PlutoniumSkipReason? skipReason = null,
        Exception? failure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        TargetName = targetName;
        Outcome = outcome;
        Duration = duration;
        SkipReason = skipReason;
        Failure = failure;
    }
}

