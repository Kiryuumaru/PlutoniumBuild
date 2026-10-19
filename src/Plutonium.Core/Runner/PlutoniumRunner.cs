using System.Diagnostics;
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
/// Plans and executes target graphs in topological order.
/// Planning is pure; execution is sequential in plan order.
/// Carries no AppId, entry, or tag knowledge.
/// </summary>
public sealed class PlutoniumRunner
{
    private readonly IReadOnlyDictionary<string, PlutoniumTargetDefinition> _targets;
    private readonly IReadOnlyList<string> _registrationOrder;
    private readonly IPlutoniumLogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumRunner"/> class.
    /// </summary>
    /// <param name="targets">Target definitions. Must not be null or empty; names must be unique (case-insensitive).</param>
    /// <param name="logger">The logger. Must not be null.</param>
    public PlutoniumRunner(IEnumerable<PlutoniumTargetDefinition> targets, IPlutoniumLogger logger)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(logger);

        var map = new Dictionary<string, PlutoniumTargetDefinition>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var target in targets)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (!map.TryAdd(target.Name, target))
            {
                throw new ArgumentException($"Duplicate target name '{target.Name}'.", nameof(targets));
            }

            order.Add(target.Name);
        }

        if (map.Count == 0)
        {
            throw new ArgumentException("At least one target is required.", nameof(targets));
        }

        _targets = map;
        _registrationOrder = order.AsReadOnly();
        _logger = logger;
    }

    /// <summary>Gets the defined target names in registration order.</summary>
    /// <param name="includeUnlisted">Whether to include unlisted targets.</param>
    /// <returns>The target names.</returns>
    public IReadOnlyList<string> ListTargets(bool includeUnlisted = false) =>
        _registrationOrder
            .Select(name => _targets[name])
            .Where(t => includeUnlisted || !t.Unlisted)
            .Select(t => t.Name)
            .ToList();

    /// <summary>
    /// Builds the execution plan: expands dependencies and triggers from the
    /// requested roots, applies skip replanning and static conditions,
    /// then orders topologically.
    /// </summary>
    /// <param name="requested">Requested target names. Must not be null.</param>
    /// <param name="skipped">Skipped target names. Must not be null; every name must match a defined target.</param>
    /// <returns>The ordered plan, excluding skipped and statically-excluded targets.</returns>
    /// <remarks>
    /// A target whose plan-time condition evaluates to false is removed from the plan
    /// and its ordering edges are dropped: dependents stay planned but lose that
    /// predecessor edge, and successors lose that trigger edge.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown on unknown targets or circular dependencies.</exception>
    public PlutoniumBuildPlan Plan(IReadOnlyList<string> requested, IReadOnlyList<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(skipped);

        var skipSet = new HashSet<string>(skipped, StringComparer.OrdinalIgnoreCase);
        foreach (var skippedName in skipSet)
        {
            if (!_targets.ContainsKey(skippedName))
            {
                throw new InvalidOperationException($"Unknown target '{skippedName}'.");
            }
        }

        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(requested);

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (skipSet.Contains(name) || !included.Add(name))
            {
                continue;
            }

            if (!_targets.TryGetValue(name, out var target))
            {
                throw new InvalidOperationException($"Unknown target '{name}'.");
            }

            foreach (var dep in target.DependsOn)
            {
                queue.Enqueue(dep);
            }

            foreach (var trigger in target.Triggers)
            {
                queue.Enqueue(trigger);
            }
        }

        var ordered = new List<PlutoniumTargetDefinition>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string name)
        {
            if (!included.Contains(name))
            {
                return;
            }

            if (inProgress.Contains(name))
            {
                throw new InvalidOperationException($"Circular dependency involving '{name}'.");
            }

            if (!visited.Add(name))
            {
                return;
            }

            inProgress.Add(name);

            var target = _targets[name];
            if (target.OnlyWhenStatic?.Invoke() == false)
            {
                included.Remove(name);
                inProgress.Remove(name);
                return;
            }

            var predecessors = target.DependsOn.Concat(target.After)
                .Where(included.Contains);
            var successors = target.Triggers.Concat(target.Before)
                .Where(included.Contains);

            foreach (var predecessor in predecessors)
            {
                Visit(predecessor);
            }

            ordered.Add(target);

            foreach (var successor in successors)
            {
                Visit(successor);
            }

            inProgress.Remove(name);
        }

        foreach (var name in _registrationOrder.Where(included.Contains))
        {
            Visit(name);
        }

        var final = ordered.Where(t => included.Contains(t.Name)).ToList();
        return new PlutoniumBuildPlan(final, skipped);
    }

    /// <summary>
    /// Plans and executes the requested targets sequentially in plan order.
    /// </summary>
    /// <param name="requested">Requested target names. Must not be null.</param>
    /// <param name="skipped">Skipped target names. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run result.</returns>
    public async Task<PlutoniumRunResult> RunAsync(
        IReadOnlyList<string> requested,
        IReadOnlyList<string> skipped,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(skipped);

        var plan = Plan(requested, skipped);
        var summaries = new List<PlutoniumTargetSummary>();
        var satisfied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = false;
        var proceedAfterFailure = false;
        var runTimer = Stopwatch.StartNew();

        foreach (var target in plan.OrderedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (failed && !proceedAfterFailure &&
                target.FailurePolicy != PlutoniumFailurePolicy.AssuredAfterFailure)
            {
                summaries.Add(new PlutoniumTargetSummary(
                    target.Name, PlutoniumTargetOutcome.Skipped,
                    TimeSpan.Zero, PlutoniumSkipReason.UpstreamNotSatisfied));
                target.WhenSkipped?.Invoke(PlutoniumSkipReason.UpstreamNotSatisfied);
                continue;
            }

            var skipReason = EvaluateSkip(target, plan, satisfied);
            if (skipReason is not null)
            {
                summaries.Add(new PlutoniumTargetSummary(
                    target.Name, PlutoniumTargetOutcome.Skipped,
                    TimeSpan.Zero, skipReason));
                target.WhenSkipped?.Invoke(skipReason.Value);
                _logger.Log(PlutoniumLogLevel.Information,
                    $"Target '{target.Name}' skipped ({skipReason}).");
                continue;
            }

            if (target.OnlyWhenDynamic?.Invoke() == false)
            {
                summaries.Add(new PlutoniumTargetSummary(
                    target.Name, PlutoniumTargetOutcome.Skipped,
                    TimeSpan.Zero, PlutoniumSkipReason.DynamicConditionFalse));
                target.WhenSkipped?.Invoke(PlutoniumSkipReason.DynamicConditionFalse);
                _logger.Log(PlutoniumLogLevel.Information,
                    $"Target '{target.Name}' skipped (dynamic condition).");
                continue;
            }

            var targetTimer = Stopwatch.StartNew();
            using (_logger.BeginScope(target.Name))
            {
                try
                {
                    _logger.Log(PlutoniumLogLevel.Information, $"Starting target '{target.Name}'.");
                    await target.Action(cancellationToken).ConfigureAwait(false);
                    var duration = targetTimer.Elapsed;
                    summaries.Add(new PlutoniumTargetSummary(
                        target.Name, PlutoniumTargetOutcome.Succeeded, duration));
                    satisfied.Add(target.Name);
                    _logger.Log(PlutoniumLogLevel.Information,
                        $"Finished target '{target.Name}' in {duration.TotalMilliseconds:F0}ms.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var duration = targetTimer.Elapsed;
                    summaries.Add(new PlutoniumTargetSummary(
                        target.Name, PlutoniumTargetOutcome.Failed, duration, failure: ex));
                    failed = true;
                    proceedAfterFailure = target.FailurePolicy == PlutoniumFailurePolicy.ProceedAfterFailure;
                    _logger.Log(PlutoniumLogLevel.Error,
                        $"Target '{target.Name}' failed: {ex.Message}");
                }
            }
        }

        return new PlutoniumRunResult(summaries, !failed, runTimer.Elapsed);
    }

    private PlutoniumSkipReason? EvaluateSkip(
        PlutoniumTargetDefinition target,
        PlutoniumBuildPlan plan,
        HashSet<string> satisfied)
    {
        if (plan.SkippedNames.Contains(target.Name, StringComparer.OrdinalIgnoreCase))
        {
            return PlutoniumSkipReason.SkippedByRequest;
        }

        foreach (var required in target.Requires)
        {
            if (!plan.OrderedTargets.Any(t => t.Name.Equals(required, StringComparison.OrdinalIgnoreCase)))
            {
                return PlutoniumSkipReason.MissingRequirement;
            }
        }

        foreach (var dep in target.DependsOn)
        {
            if (!satisfied.Contains(dep))
            {
                return PlutoniumSkipReason.UpstreamNotSatisfied;
            }
        }

        return null;
    }
}
