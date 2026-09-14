namespace Plutonium.Core;

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

/// <summary>
/// Frozen target-definition surface: identity plus dependency DSL.
/// Carries no AppId, entry, or tag knowledge; Helpers maps onto this shape.
/// </summary>
public sealed class PlutoniumTargetDefinition
{
    /// <summary>Gets the unique target name. Never null or whitespace.</summary>
    public string Name { get; }

    /// <summary>Gets the targets that must run before this target.</summary>
    public IReadOnlyList<string> DependsOn { get; }

    /// <summary>Gets the targets that run after this target completes.</summary>
    public IReadOnlyList<string> Triggers { get; }

    /// <summary>Gets the targets this target is ordered before, without depending on them.</summary>
    public IReadOnlyList<string> Before { get; }

    /// <summary>Gets the targets this target is ordered after, without depending on them.</summary>
    public IReadOnlyList<string> After { get; }

    /// <summary>Gets the plan-time condition. Null means always planned.</summary>
    public Func<bool>? OnlyWhenStatic { get; }

    /// <summary>Gets the run-time condition. Null means always executed once planned.</summary>
    public Func<bool>? OnlyWhenDynamic { get; }

    /// <summary>Gets the targets that must be present in the plan for this target to run.</summary>
    public IReadOnlyList<string> Requires { get; }

    /// <summary>Gets the action invoked when the target is skipped, if any.</summary>
    public Action<PlutoniumSkipReason>? WhenSkipped { get; }

    /// <summary>Gets the failure policy applied when this target fails.</summary>
    public PlutoniumFailurePolicy FailurePolicy { get; }

    /// <summary>Gets whether the target is hidden from listings.</summary>
    public bool Unlisted { get; }

    /// <summary>Gets the work performed by the target. Never null.</summary>
    public Func<CancellationToken, Task> Action { get; }

    /// <summary>Gets the human-readable description, if any.</summary>
    public string? Description { get; }

    private PlutoniumTargetDefinition(
        string name,
        IReadOnlyList<string> dependsOn,
        IReadOnlyList<string> triggers,
        IReadOnlyList<string> before,
        IReadOnlyList<string> after,
        Func<bool>? onlyWhenStatic,
        Func<bool>? onlyWhenDynamic,
        IReadOnlyList<string> requires,
        Action<PlutoniumSkipReason>? whenSkipped,
        PlutoniumFailurePolicy failurePolicy,
        bool unlisted,
        Func<CancellationToken, Task> action,
        string? description)
    {
        Name = name;
        DependsOn = dependsOn;
        Triggers = triggers;
        Before = before;
        After = after;
        OnlyWhenStatic = onlyWhenStatic;
        OnlyWhenDynamic = onlyWhenDynamic;
        Requires = requires;
        WhenSkipped = whenSkipped;
        FailurePolicy = failurePolicy;
        Unlisted = unlisted;
        Action = action;
        Description = description;
    }

    /// <summary>Starts a fluent definition for the named target.</summary>
    /// <param name="name">The unique target name. Must not be null or whitespace.</param>
    /// <returns>A builder for the target definition.</returns>
    public static Builder Define(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Builder(name.Trim());
    }

    /// <summary>Fluent builder for <see cref="PlutoniumTargetDefinition"/>.</summary>
    public sealed class Builder
    {
        private readonly string _name;
        private readonly List<string> _dependsOn = [];
        private readonly List<string> _triggers = [];
        private readonly List<string> _before = [];
        private readonly List<string> _after = [];
        private readonly List<string> _requires = [];
        private Func<bool>? _onlyWhenStatic;
        private Func<bool>? _onlyWhenDynamic;
        private Action<PlutoniumSkipReason>? _whenSkipped;
        private PlutoniumFailurePolicy _failurePolicy;
        private bool _unlisted;
        private Func<CancellationToken, Task>? _action;
        private string? _description;

        internal Builder(string name) => _name = name;

        /// <summary>Adds dependencies that must run before this target.</summary>
        /// <param name="names">Dependency target names.</param>
        /// <returns>This builder.</returns>
        public Builder DependsOn(params string[] names) => AddNames(_dependsOn, names, nameof(names));

        /// <summary>Adds targets that run after this target completes.</summary>
        /// <param name="names">Triggered target names.</param>
        /// <returns>This builder.</returns>
        public Builder Triggers(params string[] names) => AddNames(_triggers, names, nameof(names));

        /// <summary>Orders this target before the named targets without depending on them.</summary>
        /// <param name="names">Target names.</param>
        /// <returns>This builder.</returns>
        public Builder Before(params string[] names) => AddNames(_before, names, nameof(names));

        /// <summary>Orders this target after the named targets without depending on them.</summary>
        /// <param name="names">Target names.</param>
        /// <returns>This builder.</returns>
        public Builder After(params string[] names) => AddNames(_after, names, nameof(names));

        /// <summary>Sets the plan-time condition evaluated once during planning.</summary>
        /// <param name="condition">The condition. Must not be null.</param>
        /// <returns>This builder.</returns>
        public Builder OnlyWhenStatic(Func<bool> condition)
        {
            ArgumentNullException.ThrowIfNull(condition);
            _onlyWhenStatic = condition;
            return this;
        }

        /// <summary>Sets the run-time condition evaluated just before execution.</summary>
        /// <param name="condition">The condition. Must not be null.</param>
        /// <returns>This builder.</returns>
        public Builder OnlyWhenDynamic(Func<bool> condition)
        {
            ArgumentNullException.ThrowIfNull(condition);
            _onlyWhenDynamic = condition;
            return this;
        }

        /// <summary>Adds targets that must be present in the plan for this target to run.</summary>
        /// <param name="names">Required target names.</param>
        /// <returns>This builder.</returns>
        public Builder Requires(params string[] names) => AddNames(_requires, names, nameof(names));

        /// <summary>Sets the callback invoked when the target is skipped.</summary>
        /// <param name="onSkipped">The callback. Must not be null.</param>
        /// <returns>This builder.</returns>
        public Builder WhenSkipped(Action<PlutoniumSkipReason> onSkipped)
        {
            ArgumentNullException.ThrowIfNull(onSkipped);
            _whenSkipped = onSkipped;
            return this;
        }

        /// <summary>Sets the failure policy applied when this target fails.</summary>
        /// <param name="policy">The failure policy.</param>
        /// <returns>This builder.</returns>
        public Builder WithFailurePolicy(PlutoniumFailurePolicy policy)
        {
            _failurePolicy = policy;
            return this;
        }

        /// <summary>Hides the target from listings.</summary>
        /// <returns>This builder.</returns>
        public Builder AsUnlisted()
        {
            _unlisted = true;
            return this;
        }

        /// <summary>Sets the synchronous work performed by the target.</summary>
        /// <param name="execute">The work callback. Must not be null.</param>
        /// <returns>This builder.</returns>
        public Builder Executes(Action execute)
        {
            ArgumentNullException.ThrowIfNull(execute);
            _action = _ =>
            {
                execute();
                return Task.CompletedTask;
            };
            return this;
        }

        /// <summary>Sets the asynchronous work performed by the target.</summary>
        /// <param name="execute">The work callback. Must not be null.</param>
        /// <returns>This builder.</returns>
        public Builder ExecutesAsync(Func<CancellationToken, Task> execute)
        {
            ArgumentNullException.ThrowIfNull(execute);
            _action = execute;
            return this;
        }

        /// <summary>Sets the human-readable description.</summary>
        /// <param name="description">The description. Must not be null.</param>
        /// <returns>This builder.</returns>
        public Builder DescribedAs(string description)
        {
            ArgumentNullException.ThrowIfNull(description);
            _description = description;
            return this;
        }

        /// <summary>Builds the immutable target definition.</summary>
        /// <returns>The target definition.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no action was set.</exception>
        public PlutoniumTargetDefinition Build()
        {
            if (_action is null)
            {
                throw new InvalidOperationException($"Target '{_name}' has no action. Call Executes or ExecutesAsync.");
            }

            return new PlutoniumTargetDefinition(
                _name,
                _dependsOn.AsReadOnly(),
                _triggers.AsReadOnly(),
                _before.AsReadOnly(),
                _after.AsReadOnly(),
                _onlyWhenStatic,
                _onlyWhenDynamic,
                _requires.AsReadOnly(),
                _whenSkipped,
                _failurePolicy,
                _unlisted,
                _action,
                _description);
        }

        private Builder AddNames(List<string> sink, string[] names, string paramName)
        {
            ArgumentNullException.ThrowIfNull(names);
            foreach (var name in names)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name, paramName);
                sink.Add(name.Trim());
            }

            return this;
        }
    }
}
