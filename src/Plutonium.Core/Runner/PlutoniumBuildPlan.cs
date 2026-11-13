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
/// Topologically ordered execution plan produced before any target runs.
/// Skipped and statically-excluded targets are already removed.
/// </summary>
public sealed class PlutoniumBuildPlan
{
    /// <summary>Gets the ordered targets to execute.</summary>
    public IReadOnlyList<PlutoniumTargetDefinition> OrderedTargets { get; }

    /// <summary>Gets the names excluded via skip requests.</summary>
    public IReadOnlyList<string> SkippedNames { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumBuildPlan"/> class.
    /// </summary>
    /// <param name="orderedTargets">Ordered targets. Must not be null.</param>
    /// <param name="skippedNames">Skipped names. Must not be null.</param>
    public PlutoniumBuildPlan(
        IReadOnlyList<PlutoniumTargetDefinition> orderedTargets,
        IReadOnlyList<string> skippedNames)
    {
        ArgumentNullException.ThrowIfNull(orderedTargets);
        ArgumentNullException.ThrowIfNull(skippedNames);
        OrderedTargets = orderedTargets;
        SkippedNames = skippedNames;
    }
}

