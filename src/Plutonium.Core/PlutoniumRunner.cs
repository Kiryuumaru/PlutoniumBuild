using System.Diagnostics;

namespace Plutonium.Core;

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
