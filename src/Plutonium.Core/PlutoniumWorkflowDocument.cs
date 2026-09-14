namespace Plutonium.Core;

/// <summary>
/// Opaque workflow document: a name, an opaque trigger block, ordered jobs,
/// and workflow-level environment. Carries no entry, AppId, or tag knowledge;
/// Helpers owns trigger sets and per-entry fan-out and renders them into strings.
/// </summary>
public sealed class PlutoniumWorkflowDocument
{
    /// <summary>Gets the workflow name. Never null or whitespace.</summary>
    public string Name { get; }

    /// <summary>Gets the opaque pre-rendered trigger block (the <c>on:</c> body). Never null.</summary>
    public string Triggers { get; }

    /// <summary>Gets the ordered jobs. Never null and never empty.</summary>
    public IReadOnlyList<PlutoniumWorkflowJob> Jobs { get; }

    /// <summary>Gets the workflow-level environment variables. Never null.</summary>
    public IReadOnlyDictionary<string, string> Env { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumWorkflowDocument"/> class.
    /// </summary>
    /// <param name="name">The workflow name. Must not be null or whitespace.</param>
    /// <param name="triggers">The opaque trigger block. Must not be null.</param>
    /// <param name="jobs">The ordered jobs. Must not be null or empty; entries must not be null and ids must be unique (case-insensitive).</param>
    /// <param name="env">Workflow-level environment variables. Must not be null; keys and values must not be null.</param>
    public PlutoniumWorkflowDocument(
        string name,
        string triggers,
        IEnumerable<PlutoniumWorkflowJob> jobs,
        IReadOnlyDictionary<string, string>? env = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(jobs);
        Name = name.Trim();
        Triggers = triggers;

        var materialized = jobs.Select(job =>
        {
            ArgumentNullException.ThrowIfNull(job, nameof(jobs));
            return job;
        }).ToList();
        if (materialized.Count == 0)
        {
            throw new ArgumentException("At least one job is required.", nameof(jobs));
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in materialized)
        {
            if (!ids.Add(job.Id))
            {
                throw new ArgumentException($"Duplicate job id '{job.Id}'.", nameof(jobs));
            }
        }

        Jobs = materialized;

        if (env is null)
        {
            Env = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in env)
            {
                ArgumentNullException.ThrowIfNull(pair.Key, nameof(env));
                ArgumentNullException.ThrowIfNull(pair.Value, nameof(env));
                variables.Add(pair.Key, pair.Value);
            }

            Env = variables;
        }
    }

    /// <summary>
    /// Renders the workflow to GitHub Actions YAML text with LF newlines and a trailing newline.
    /// </summary>
    /// <returns>YAML text. Never null.</returns>
    public string WriteYaml()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("name: ").Append(PlutoniumYaml.QuoteString(Name)).Append('\n');
        builder.Append("on:\n");
        builder.Append(PlutoniumYaml.IndentRaw(Triggers.Trim('\n', '\r'), 1)).Append('\n');
        if (Env.Count > 0)
        {
            builder.Append("env:\n");
            foreach (var pair in Env)
            {
                builder.Append("  ").Append(PlutoniumYaml.QuoteString(pair.Key)).Append(": ").Append(PlutoniumYaml.QuoteString(pair.Value)).Append('\n');
            }
        }

        builder.Append("jobs:\n");
        foreach (var job in Jobs)
        {
            builder.Append("  ").Append(PlutoniumYaml.QuoteString(job.Id)).Append(":\n");
            builder.Append("    runs-on: ").Append(PlutoniumYaml.QuoteString(job.RunsOn)).Append('\n');
            if (job.Needs.Count > 0)
            {
                builder.Append("    needs: [");
                builder.Append(string.Join(", ", job.Needs.Select(PlutoniumYaml.QuoteString)));
                builder.Append("]\n");
            }

            if (job.IfCondition is not null)
            {
                builder.Append("    if: ").Append(job.IfCondition).Append('\n');
            }

            if (job.Env.Count > 0)
            {
                builder.Append("    env:\n");
                foreach (var pair in job.Env)
                {
                    builder.Append("      ").Append(PlutoniumYaml.QuoteString(pair.Key)).Append(": ").Append(PlutoniumYaml.QuoteString(pair.Value)).Append('\n');
                }
            }

            if (job.Outputs.Count > 0)
            {
                builder.Append("    outputs:\n");
                foreach (var pair in job.Outputs)
                {
                    builder.Append("      ").Append(PlutoniumYaml.QuoteString(pair.Key)).Append(": ").Append(pair.Value).Append('\n');
                }
            }

            builder.Append("    steps:\n");
            if (job.Steps.Count == 0)
            {
                builder.Append("      []\n");
            }
            else
            {
                foreach (var step in job.Steps)
                {
                    builder.Append(PlutoniumYaml.IndentRaw(step.Trim('\n', '\r'), 3)).Append('\n');
                }
            }
        }

        return builder.ToString();
    }
}
