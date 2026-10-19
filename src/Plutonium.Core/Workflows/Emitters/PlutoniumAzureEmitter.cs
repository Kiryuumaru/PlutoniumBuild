using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;

namespace Plutonium.Core.Workflows.Emitters;

/// <summary>
/// Azure Pipelines workflow emitter over opaque strings: writes
/// <c>azure-pipelines.yml</c> with opaque trigger, pr, pool, and step blocks.
/// Carries no entry, AppId, or tag knowledge.
/// </summary>
/// <remarks>
/// The detected host only selects emitted grouping hints; <see cref="PlutoniumHostKind.Local"/>
/// is the documented fallback and emits the same YAML with no host-specific markers.
/// Step shapes use <c>script</c>/<c>name</c>/<c>displayName</c> keys.
/// Job outputs render as a <c>variables:</c> block alongside job environment.
/// </remarks>
public static class PlutoniumAzureEmitter
{
    /// <summary>
    /// Builds an opaque checkout step block.
    /// </summary>
    /// <param name="fetchDepth">Fetch depth text (for example <c>0</c>). Must not be null or whitespace.</param>
    /// <param name="submodules">Whether to checkout submodules, rendered as <c>submodules</c>.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string CheckoutStep(string fetchDepth, bool submodules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fetchDepth);
        return "- checkout: self\n" +
            $"  fetchDepth: {PlutoniumYaml.QuoteString(fetchDepth.Trim())}\n" +
            $"  submodules: {(submodules ? "true" : "false")}";
    }

    /// <summary>
    /// Builds an opaque cache step block.
    /// </summary>
    /// <param name="key">Cache key expression. Must not be null or whitespace.</param>
    /// <param name="path">Cache path. Must not be null or whitespace.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string CacheStep(string key, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return "- task: Cache@2\n" +
            "  inputs:\n" +
            $"    key: {PlutoniumYaml.QuoteString(key.Trim())}\n" +
            $"    path: {PlutoniumYaml.QuoteString(path.Trim())}";
    }

    /// <summary>
    /// Builds an opaque script step block.
    /// </summary>
    /// <remarks>
    /// The step <c>name:</c> is the output-reference key for values exported via
    /// <see cref="PlutoniumHost.ExportJobOutput"/>; the method writes them with
    /// <c>isOutput=true</c> so downstream jobs address them through this name.
    /// </remarks>
    /// <param name="displayName">The display name. Must not be null or whitespace.</param>
    /// <param name="script">The script body. Must not be null or whitespace.</param>
    /// <param name="condition">The step condition, or null for unconditional.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string ScriptStep(string displayName, string script, string? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var builder = new System.Text.StringBuilder();
        builder.Append("- script: |\n");
        foreach (var line in script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim('\n', '\r').Split('\n'))
        {
            builder.Append("    ").Append(line.TrimEnd()).Append('\n');
        }

        builder.Append("  displayName: ").Append(PlutoniumYaml.QuoteString(displayName.Trim())).Append('\n');
        builder.Append("  name: ").Append(PlutoniumYaml.QuoteString(ToStepName(displayName)));
        if (!string.IsNullOrWhiteSpace(condition))
        {
            builder.Append('\n').Append("  condition: ").Append(condition.Trim());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds an opaque publish step block.
    /// </summary>
    /// <param name="path">The artifact path. Must not be null or whitespace.</param>
    /// <param name="artifact">The artifact name. Must not be null or whitespace.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string PublishStep(string path, string artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact);
        return "- publish: " + PlutoniumYaml.QuoteString(path.Trim()) + "\n" +
            $"  artifact: {PlutoniumYaml.QuoteString(artifact.Trim())}";
    }

    /// <summary>
    /// Renders the workflow to Azure Pipelines YAML text.
    /// </summary>
    /// <param name="document">The workflow document. Must not be null; jobs render as stages.</param>
    /// <param name="trigger">Opaque trigger block (the <c>trigger:</c> body), or null to omit.</param>
    /// <param name="pr">Opaque pr block (the <c>pr:</c> body), or null to omit.</param>
    /// <param name="pool">Opaque pool block (the <c>pool:</c> body), or null to omit.</param>
    /// <param name="host">The host kind; detected via <see cref="PlutoniumHost.Detect"/> when null. Selects grouping hints only; Local fallback emits the same YAML with no host-specific markers.</param>
    /// <returns>YAML text. Never null.</returns>
    public static string Render(
        PlutoniumWorkflowDocument document,
        string? trigger = null,
        string? pr = null,
        string? pool = null,
        PlutoniumHostKind? host = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = host ?? PlutoniumHost.Detect();
        var builder = new System.Text.StringBuilder();
        builder.Append("name: ").Append(PlutoniumYaml.QuoteString(document.Name)).Append('\n');
        if (string.IsNullOrWhiteSpace(trigger))
        {
            builder.Append("trigger: none\n");
        }
        else
        {
            builder.Append("trigger:\n");
            builder.Append(PlutoniumYaml.IndentRaw(trigger.Trim('\n', '\r'), 1)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(pr))
        {
            builder.Append("pr:\n");
            builder.Append(PlutoniumYaml.IndentRaw(pr.Trim('\n', '\r'), 1)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(pool))
        {
            builder.Append("pool:\n");
            builder.Append(PlutoniumYaml.IndentRaw(pool.Trim('\n', '\r'), 1)).Append('\n');
        }

        if (document.Env.Count > 0)
        {
            builder.Append("variables:\n");
            foreach (var pair in document.Env)
            {
                builder.Append("  ").Append(PlutoniumYaml.QuoteString(pair.Key)).Append(": ").Append(PlutoniumYaml.QuoteString(pair.Value)).Append('\n');
            }
        }

        builder.Append("stages:\n");
        foreach (var job in document.Jobs)
        {
            builder.Append("- stage: ").Append(PlutoniumYaml.QuoteString(ToStageId(job.Id))).Append('\n');
            if (job.IfCondition is not null)
            {
                builder.Append("  condition: ").Append(job.IfCondition).Append('\n');
            }

            if (job.Needs.Count > 0)
            {
                builder.Append("  dependsOn: [");
                builder.Append(string.Join(", ", job.Needs.Select(ToStageId).Select(PlutoniumYaml.QuoteString)));
                builder.Append("]\n");
            }

            builder.Append("  jobs:\n");
            builder.Append("  - job: ").Append(PlutoniumYaml.QuoteString(ToStageId(job.Id))).Append('\n');
            builder.Append("    pool: ").Append(PlutoniumYaml.QuoteString(job.RunsOn)).Append('\n');
            if (job.Env.Count > 0 || job.Outputs.Count > 0)
            {
                builder.Append("    variables:\n");
                foreach (var pair in job.Env)
                {
                    builder.Append("      ").Append(PlutoniumYaml.QuoteString(pair.Key)).Append(": ").Append(PlutoniumYaml.QuoteString(pair.Value)).Append('\n');
                }

                foreach (var pair in job.Outputs)
                {
                    builder.Append("      ").Append(PlutoniumYaml.QuoteString(pair.Key)).Append(": ").Append(PlutoniumYaml.QuoteString(pair.Value)).Append('\n');
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

    /// <summary>
    /// Writes <c>azure-pipelines.yml</c> under the repository root.
    /// </summary>
    /// <param name="repositoryRoot">The repository root directory. Must not be null.</param>
    /// <param name="document">The workflow document. Must not be null.</param>
    /// <param name="trigger">Opaque trigger block, or null for <c>trigger: none</c>.</param>
    /// <param name="pr">Opaque pr block, or null to omit.</param>
    /// <param name="pool">Opaque pool block, or null to omit.</param>
    /// <param name="host">The host kind; detected via <see cref="PlutoniumHost.Detect"/> when null. Selects grouping hints only; Local fallback emits the same YAML with no host-specific markers.</param>
    /// <returns>The written file path. Never null.</returns>
    public static PlutoniumPath WriteWorkflow(
        PlutoniumPath repositoryRoot,
        PlutoniumWorkflowDocument document,
        string? trigger = null,
        string? pr = null,
        string? pool = null,
        PlutoniumHostKind? host = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(document);
        var target = repositoryRoot.Combine("azure-pipelines.yml");
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(target.Value));
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        System.IO.File.WriteAllText(target.Value, Render(document, trigger, pr, pool, host));
        return target;
    }

    private static string ToStepName(string displayName)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in displayName.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        var name = builder.ToString().Trim('_');
        return name.Length == 0 ? "step" : name;
    }

    private static string ToStageId(string jobId)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in jobId.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        var stage = builder.ToString().Trim('_');
        return stage.Length == 0 ? "job" : stage.ToUpperInvariant();
    }
}
