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
    /// <summary>
    /// Builds an opaque checkout step block.
    /// </summary>
    /// <param name="fetchDepth">Fetch depth text (for example <c>0</c>). Must not be null or whitespace.</param>
    /// <param name="fetchTags">Whether to fetch tags, rendered as <c>fetch-tags</c>.</param>
    /// <param name="submodules">Submodules mode text (for example <c>recursive</c>). Must not be null or whitespace.</param>
    /// <returns>Opaque step YAML. Never null.</returns>
    public static string CheckoutStep(string fetchDepth, bool fetchTags, string submodules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fetchDepth);
        ArgumentException.ThrowIfNullOrWhiteSpace(submodules);
        return "- uses: actions/checkout@v4\n" +
            "  with:\n" +
            $"    fetch-depth: {PlutoniumYaml.QuoteString(fetchDepth.Trim())}\n" +
            $"    fetch-tags: {(fetchTags ? "true" : "false")}\n" +
            $"    submodules: {PlutoniumYaml.QuoteString(submodules.Trim())}";
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
        builder.Append("- uses: actions/cache@v4\n  with:\n");
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
        return "- uses: actions/upload-artifact@v4\n" +
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
        return "- uses: actions/download-artifact@v4\n" +
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
        var target = repositoryRoot.Combine(".github", "workflows", fileName.Trim() + ".yml");
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
