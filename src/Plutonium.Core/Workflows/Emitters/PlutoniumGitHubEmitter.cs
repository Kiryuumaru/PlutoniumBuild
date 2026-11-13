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
/// GitHub Actions workflow emitter over opaque strings: writes
/// <c>.github/workflows/&lt;name&gt;.yml</c> from a <see cref="PlutoniumWorkflowDocument"/>
/// plus opaque concurrency and permissions blocks. Carries no entry, AppId, or tag knowledge.
/// </summary>
/// <remarks>
/// The detected host only selects emitted grouping hints; <see cref="PlutoniumHostKind.Local"/>
/// is the documented fallback and emits the same YAML with no host-specific markers.
/// </remarks>
public static class PlutoniumGitHubEmitter
{
    private static readonly IReadOnlyDictionary<string, string> PinnedActions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["actions/checkout"] = "actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0",
        ["actions/cache"] = "actions/cache@0057852bfaa89a56745cba8c7296529d2fc39830 # v4.3.0",
        ["actions/upload-artifact"] = "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2",
        ["actions/download-artifact"] = "actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4.3.0",
    };

    /// <summary>
    /// Builds an opaque checkout step block.
    /// </summary>
    /// <param name="fetchDepth">Fetch depth text (for example <c>0</c>). Must not be null or whitespace.</param>
    /// <param name="fetchTags">Whether to fetch tags, rendered as <c>fetch-tags</c>.</param>
    /// <param name="submodules">Submodules mode text (for example <c>recursive</c>). Must not be null or whitespace.</param>
    /// <param name="persistCredentials">Whether later steps may use the checkout token, rendered as <c>persist-credentials</c>.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string CheckoutStep(string fetchDepth, bool fetchTags, string submodules, bool persistCredentials = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fetchDepth);
        ArgumentException.ThrowIfNullOrWhiteSpace(submodules);
        return "- uses: " + PinnedActions["actions/checkout"] + "\n" +
            "  with:\n" +
            $"    fetch-depth: {PlutoniumYaml.QuoteString(fetchDepth.Trim())}\n" +
            $"    fetch-tags: {(fetchTags ? "true" : "false")}\n" +
            $"    submodules: {PlutoniumYaml.QuoteString(submodules.Trim())}\n" +
            $"    persist-credentials: {(persistCredentials ? "true" : "false")}";
    }

    /// <summary>
    /// Builds an opaque cache step block.
    /// </summary>
    /// <param name="path">Cache path. Must not be null or whitespace.</param>
    /// <param name="key">Cache key expression. Must not be null or whitespace.</param>
    /// <param name="restoreKeys">Restore-key expressions. Must not be null; entries must not be null.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string CacheStep(string path, string key, IEnumerable<string> restoreKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(restoreKeys);
        var keys = restoreKeys.Select(restoreKey =>
        {
            ArgumentNullException.ThrowIfNull(restoreKey, nameof(restoreKeys));
            return restoreKey;
        }).ToList();
        var builder = new System.Text.StringBuilder();
        builder.Append("- uses: ").Append(PinnedActions["actions/cache"]).Append("\n  with:\n");
        builder.Append("    path: ").Append(PlutoniumYaml.QuoteString(path.Trim())).Append('\n');
        builder.Append("    key: ").Append(PlutoniumYaml.QuoteString(key.Trim()));
        if (keys.Count > 0)
        {
            builder.Append("\n    restore-keys: |\n");
            foreach (var restoreKey in keys)
            {
                builder.Append("      ").Append(restoreKey.Trim()).Append('\n');
            }

            return builder.ToString().TrimEnd('\n', '\r');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds an opaque run step block.
    /// </summary>
    /// <remarks>
    /// The optional step id is the output-reference key for values exported via
    /// <see cref="PlutoniumHost.ExportJobOutput"/>; downstream jobs address them
    /// through the step id.
    /// </remarks>
    /// <param name="name">The step name. Must not be null or whitespace.</param>
    /// <param name="run">The run script. Must not be null or whitespace.</param>
    /// <param name="condition">The step condition, or null for unconditional.</param>
    /// <param name="stepId">The optional step id, sanitized to letters, digits, underscore, and hyphen. Null or whitespace to omit.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stepId"/> contains no letters, digits, underscore, or hyphen.</exception>
    public static string RunStep(string name, string run, string? condition = null, string? stepId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        var builder = new System.Text.StringBuilder();
        builder.Append("- name: ").Append(PlutoniumYaml.QuoteString(name.Trim())).Append('\n');
        if (!string.IsNullOrWhiteSpace(stepId))
        {
            builder.Append("  id: ").Append(SanitizeStepId(stepId)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(condition))
        {
            builder.Append("  if: ").Append(condition.Trim()).Append('\n');
        }

        builder.Append("  run: |\n");
        foreach (var line in run.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim('\n', '\r').Split('\n'))
        {
            builder.Append("    ").Append(line.TrimEnd()).Append('\n');
        }

        return builder.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Builds an opaque upload-artifact step block.
    /// </summary>
    /// <param name="name">The artifact name. Must not be null or whitespace.</param>
    /// <param name="path">The artifact path. Must not be null or whitespace.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string UploadArtifactStep(string name, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return "- uses: " + PinnedActions["actions/upload-artifact"] + "\n" +
            "  with:\n" +
            $"    name: {PlutoniumYaml.QuoteString(name.Trim())}\n" +
            $"    path: {PlutoniumYaml.QuoteString(path.Trim())}";
    }

    /// <summary>
    /// Builds an opaque download-artifact step block.
    /// </summary>
    /// <param name="name">The artifact name. Must not be null or whitespace.</param>
    /// <param name="path">The destination path. Must not be null or whitespace.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string DownloadArtifactStep(string name, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return "- uses: " + PinnedActions["actions/download-artifact"] + "\n" +
            "  with:\n" +
            $"    name: {PlutoniumYaml.QuoteString(name.Trim())}\n" +
            $"    path: {PlutoniumYaml.QuoteString(path.Trim())}";
    }

    /// <summary>
    /// Renders the workflow to YAML text with opaque concurrency and permissions blocks spliced in.
    /// </summary>
    /// <param name="document">The workflow document. Must not be null.</param>
    /// <param name="concurrency">Opaque concurrency block (the <c>concurrency:</c> body), or null to omit.</param>
    /// <param name="permissions">Opaque permissions block (the <c>permissions:</c> body), or null to omit.</param>
    /// <param name="host">The host kind; detected via <see cref="PlutoniumHost.Detect"/> when null. Selects grouping hints only; Local fallback emits the same YAML with no host-specific markers.</param>
    /// <returns>YAML text. Never null.</returns>
    public static string Render(
        PlutoniumWorkflowDocument document,
        string? concurrency = null,
        string? permissions = null,
        PlutoniumHostKind? host = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = host ?? PlutoniumHost.Detect();
        var yaml = document.WriteYaml().TrimEnd('\n', '\r');
        var builder = new System.Text.StringBuilder();
        builder.Append(yaml).Append('\n');
        if (!string.IsNullOrWhiteSpace(concurrency))
        {
            builder.Append("concurrency:\n");
            builder.Append(PlutoniumYaml.IndentRaw(concurrency.Trim('\n', '\r'), 1)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(permissions))
        {
            builder.Append("permissions:\n");
            builder.Append(PlutoniumYaml.IndentRaw(permissions.Trim('\n', '\r'), 1)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes the workflow file to <c>.github/workflows/&lt;fileName&gt;.yml</c> under the repository root.
    /// </summary>
    /// <param name="repositoryRoot">The repository root directory. Must not be null.</param>
    /// <param name="fileName">The workflow file name without extension. Must not be null or whitespace.</param>
    /// <param name="document">The workflow document. Must not be null.</param>
    /// <param name="concurrency">Opaque concurrency block, or null to omit.</param>
    /// <param name="permissions">Opaque permissions block, or null to omit.</param>
    /// <param name="host">The host kind; detected via <see cref="PlutoniumHost.Detect"/> when null. Selects grouping hints only; Local fallback emits the same YAML with no host-specific markers.</param>
    /// <returns>The written workflow file path. Never null.</returns>
    /// <exception cref="ArgumentException">Thrown when the file name resolves outside <c>.github/workflows</c>.</exception>
    public static PlutoniumPath WriteWorkflow(
        PlutoniumPath repositoryRoot,
        string fileName,
        PlutoniumWorkflowDocument document,
        string? concurrency = null,
        string? permissions = null,
        PlutoniumHostKind? host = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(document);
        var workflowsRoot = repositoryRoot.Combine(".github", "workflows");
        var target = workflowsRoot.Combine(fileName.Trim() + ".yml");
        var root = System.IO.Path.GetFullPath(workflowsRoot.Value) + System.IO.Path.DirectorySeparatorChar;
        if (!System.IO.Path.GetFullPath(target.Value).StartsWith(root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Workflow file name must resolve under .github/workflows.", nameof(fileName));
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(target.Value));
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        System.IO.File.WriteAllText(target.Value, Render(document, concurrency, permissions, host));
        return target;
    }

    private static string SanitizeStepId(string stepId)
    {
        var builder = new System.Text.StringBuilder(stepId.Trim().Length);
        foreach (var ch in stepId.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
        }

        var sanitized = builder.ToString().Trim('_', '-');
        if (sanitized.Length == 0)
        {
            throw new ArgumentException("Step id must contain at least one letter or digit.", nameof(stepId));
        }

        return sanitized;
    }
}
