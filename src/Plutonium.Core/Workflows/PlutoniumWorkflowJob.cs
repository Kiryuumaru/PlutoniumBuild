using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Workflows;

/// <summary>
/// Opaque workflow job model: identity, runner image as a string, and
/// pre-rendered step blocks. Carries no entry, AppId, or tag knowledge;
/// Helpers maps RunnerOS and entry matrices onto these strings.
/// </summary>
public sealed class PlutoniumWorkflowJob
{
    /// <summary>Gets the job id. Never null or whitespace.</summary>
    public string Id { get; }

    /// <summary>Gets the runner image expression (for example <c>ubuntu-latest</c>). Never null or whitespace.</summary>
    public string RunsOn { get; }

    /// <summary>Gets the opaque pre-rendered step blocks in order. Never null.</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>Gets the ids of jobs this job depends on. Never null.</summary>
    public IReadOnlyList<string> Needs { get; }

    /// <summary>Gets the condition expression, or null for unconditional. Null when absent.</summary>
    public string? IfCondition { get; }

    /// <summary>Gets the job-level environment variables. Never null.</summary>
    public IReadOnlyDictionary<string, string> Env { get; }

    /// <summary>Gets the job outputs (name to value expression). Never null.</summary>
    public IReadOnlyDictionary<string, string> Outputs { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumWorkflowJob"/> class.
    /// </summary>
    /// <param name="id">The job id. Must not be null or whitespace.</param>
    /// <param name="runsOn">The runner image expression. Must not be null or whitespace.</param>
    /// <param name="steps">Opaque step blocks. Must not be null; each entry must not be null.</param>
    /// <param name="needs">Job ids this job depends on. Must not be null; each entry must not be null or whitespace.</param>
    /// <param name="ifCondition">The condition expression, or null for unconditional.</param>
    /// <param name="env">Job-level environment variables. Must not be null; keys and values must not be null.</param>
    /// <param name="outputs">Job outputs. Must not be null; keys and values must not be null.</param>
    public PlutoniumWorkflowJob(
        string id,
        string runsOn,
        IEnumerable<string> steps,
        IEnumerable<string>? needs = null,
        string? ifCondition = null,
        IReadOnlyDictionary<string, string>? env = null,
        IReadOnlyDictionary<string, string>? outputs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(runsOn);
        ArgumentNullException.ThrowIfNull(steps);
        Id = id.Trim();
        RunsOn = runsOn.Trim();
        Steps = steps.Select(step =>
        {
            ArgumentNullException.ThrowIfNull(step, nameof(steps));
            return step;
        }).ToList();

        if (needs is null)
        {
            Needs = [];
        }
        else
        {
            Needs = needs.Select(need =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(need, nameof(needs));
                return need.Trim();
            }).ToList();
        }

        IfCondition = string.IsNullOrWhiteSpace(ifCondition) ? null : ifCondition.Trim();

        if (env is null)
        {
            Env = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var materialized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in env)
            {
                ArgumentNullException.ThrowIfNull(pair.Key, nameof(env));
                ArgumentNullException.ThrowIfNull(pair.Value, nameof(env));
                materialized.Add(pair.Key, pair.Value);
            }

            Env = materialized;
        }

        if (outputs is null)
        {
            Outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var materialized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in outputs)
            {
                ArgumentNullException.ThrowIfNull(pair.Key, nameof(outputs));
                ArgumentNullException.ThrowIfNull(pair.Value, nameof(outputs));
                materialized.Add(pair.Key, pair.Value);
            }

            Outputs = materialized;
        }
    }
}
