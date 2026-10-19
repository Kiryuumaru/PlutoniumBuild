using System.Text.Json;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;
using Plutonium.Helpers.Entries;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Pipelines;

/// <summary>
/// Pipeline detection: selects the pipeline from the Core host, parses CI refs into
/// pipeline info, and wires secrets plus the run script from RunnerOS.
/// Core stays opaque; Helpers owns branch/tag/PR parsing and payload plumbing.
/// </summary>
public static class PlutoniumPipelineDetection
{
    /// <summary>
    /// Selects the pipeline type from the detected Core host.
    /// </summary>
    /// <returns>The pipeline type.</returns>
    public static PlutoniumPipelineType DetectPipelineType() => PlutoniumHost.Detect().ToPipelineType();

    /// <summary>
    /// Creates the pipeline for the detected host.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pipeline plus its info. Never null.</returns>
    public static async Task<PlutoniumPipelineRun> SetupPipelineAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IPlutoniumPipeline pipeline = context.PipelineType switch
        {
            PlutoniumPipelineType.Azure => new PlutoniumAzurePipeline(),
            PlutoniumPipelineType.GitHub => new PlutoniumGitHubPipeline(),
            _ => new PlutoniumLocalPipeline(),
        };
        var type = pipeline.PipelineType;
        context.PipelineType = type;
        var info = await pipeline.GetPipelineInfoAsync(context, cancellationToken).ConfigureAwait(false);
        return new PlutoniumPipelineRun(pipeline, info, type);
    }

    /// <summary>
    /// Parses a GitHub ref (<c>GITHUB_REF</c>) into pipeline info.
    /// </summary>
    /// <param name="gitHubRef">The ref value. May be null or empty for the fallback branch.</param>
    /// <param name="gitHubBaseRef">The base ref for pull requests. May be null.</param>
    /// <param name="fallbackBranch">The fallback branch when the ref is missing. Must not be null or whitespace.</param>
    /// <param name="containingBranch">Optional branch containing a non-bump tag, resolved at GetPipelineInfo time via <c>git branch -r --contains</c>. May be null for the fallback branch.</param>
    /// <returns>The pipeline info. Never null.</returns>
    public static PlutoniumPipelineInfo ParseGitHubRef(
        string? gitHubRef,
        string? gitHubBaseRef,
        string fallbackBranch,
        string? containingBranch = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackBranch);
        if (string.IsNullOrWhiteSpace(gitHubRef))
        {
            return new PlutoniumPipelineInfo(fallbackBranch.Trim(), PlutoniumTriggerType.Commit);
        }

        var value = gitHubRef.Trim();
        if (value.StartsWith("refs/pull", StringComparison.OrdinalIgnoreCase))
        {
            var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var number = segments.Length >= 3 && long.TryParse(segments[2], out var parsed) ? parsed : 0;
            var branch = string.IsNullOrWhiteSpace(gitHubBaseRef) ? fallbackBranch.Trim() : gitHubBaseRef.Trim();
            return new PlutoniumPipelineInfo(branch, PlutoniumTriggerType.PullRequest, number);
        }

        if (value.StartsWith("refs/tags", StringComparison.OrdinalIgnoreCase))
        {
            if (value.StartsWith("refs/tags/bump-", StringComparison.OrdinalIgnoreCase))
            {
                return new PlutoniumPipelineInfo(value[15..].Trim(), PlutoniumTriggerType.Tag);
            }

            var branch = string.IsNullOrWhiteSpace(containingBranch) ? fallbackBranch.Trim() : containingBranch.Trim();
            return new PlutoniumPipelineInfo(branch, PlutoniumTriggerType.Tag);
        }

        if (value.StartsWith("refs/heads", StringComparison.OrdinalIgnoreCase))
        {
            return new PlutoniumPipelineInfo(value[11..].Trim(), PlutoniumTriggerType.Commit);
        }

        return new PlutoniumPipelineInfo(fallbackBranch.Trim(), PlutoniumTriggerType.Commit);
    }

    /// <summary>
    /// Parses an Azure source branch (<c>BUILD_SOURCEBRANCH</c>) into pipeline info.
    /// </summary>
    /// <param name="sourceBranch">The source branch value. May be null or empty for the fallback branch.</param>
    /// <param name="pullRequestNumber">The pull-request number text. May be null.</param>
    /// <param name="targetBranch">The target branch for pull requests. May be null.</param>
    /// <param name="fallbackBranch">The fallback branch when the value is missing. Must not be null or whitespace.</param>
    /// <param name="containingBranch">Optional branch containing a non-bump tag, resolved at GetPipelineInfo time via <c>git branch -r --contains</c>. May be null for the fallback branch.</param>
    /// <returns>The pipeline info. Never null.</returns>
    public static PlutoniumPipelineInfo ParseAzureBranch(
        string? sourceBranch,
        string? pullRequestNumber,
        string? targetBranch,
        string fallbackBranch,
        string? containingBranch = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackBranch);
        if (string.IsNullOrWhiteSpace(sourceBranch))
        {
            return new PlutoniumPipelineInfo(fallbackBranch.Trim(), PlutoniumTriggerType.Commit);
        }

        var value = sourceBranch.Trim();
        if (value.StartsWith("refs/pull", StringComparison.OrdinalIgnoreCase))
        {
            var number = long.TryParse((pullRequestNumber ?? string.Empty).Trim(), out var parsed) ? parsed : 0;
            var branch = string.IsNullOrWhiteSpace(targetBranch) ? fallbackBranch.Trim() : targetBranch.Trim();
            return new PlutoniumPipelineInfo(branch, PlutoniumTriggerType.PullRequest, number);
        }

        if (value.StartsWith("refs/tags", StringComparison.OrdinalIgnoreCase))
        {
            if (value.StartsWith("refs/tags/bump-", StringComparison.OrdinalIgnoreCase))
            {
                return new PlutoniumPipelineInfo(value[15..].Trim(), PlutoniumTriggerType.Tag);
            }

            var branch = string.IsNullOrWhiteSpace(containingBranch) ? fallbackBranch.Trim() : containingBranch.Trim();
            return new PlutoniumPipelineInfo(branch, PlutoniumTriggerType.Tag);
        }

        if (value.StartsWith("refs/heads", StringComparison.OrdinalIgnoreCase))
        {
            return new PlutoniumPipelineInfo(value[11..].Trim(), PlutoniumTriggerType.Commit);
        }

        return new PlutoniumPipelineInfo(fallbackBranch.Trim(), PlutoniumTriggerType.Commit);
    }

    /// <summary>
    /// Builds the workflow-level environment map: the pipeline token plus one entry per declared secret.
    /// </summary>
    /// <param name="build">The build instance. Must not be null.</param>
    /// <param name="tokenName">The token variable name. Must not be null or whitespace.</param>
    /// <param name="tokenExpression">The token expression. Must not be null or whitespace.</param>
    /// <param name="secretExpression">Maps a secret name to its expression. Must not be null.</param>
    /// <returns>The environment map. Never null.</returns>
    public static IReadOnlyDictionary<string, string> BuildWorkflowEnv(
        object build,
        string tokenName,
        string tokenExpression,
        Func<string, string> secretExpression)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenExpression);
        ArgumentNullException.ThrowIfNull(secretExpression);
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [tokenName.Trim()] = tokenExpression.Trim(),
        };
        foreach (var declaration in PlutoniumSecrets.GetSecretVariables(build))
        {
            env[declaration.EnvironmentVariableName] = secretExpression(declaration.Secret.SecretVariableName);
        }

        return env;
    }

    /// <summary>
    /// Builds the opaque per-entry environment exports under the legacy full-matrix contract.
    /// One <c>CONDITION/RUNS_ON/RUN_SCRIPT/CACHE_*/CHECKOUT_*</c> set per entry id.
    /// </summary>
    /// <remarks>
    /// Reserved legacy contract: no live caller. The narrowed Option C contract is the
    /// only live one: <c>CONDITION</c> plus <c>BUILD_ID</c>/<c>HAS_RELEASE</c> job
    /// outputs wired by <c>FinalizePreSetupAsync</c>, with static runs-on/run-script/checkout
    /// values baked at generation via <c>EntrySetupMap</c>.
    /// </remarks>
    /// <param name="preSetup">The computed payload. Must not be null.</param>
    /// <returns>Exports keyed by entry id then variable name. Never null.</returns>
    [Obsolete("Reserved legacy full-matrix contract. The narrowed Option C contract (CONDITION plus BUILD_ID/HAS_RELEASE job outputs) is the only live one.", false)]
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildEntryExports(PlutoniumPipelinePreSetup preSetup)
    {
        ArgumentNullException.ThrowIfNull(preSetup);
        var runKind = preSetup.TriggerType == PlutoniumTriggerType.PullRequest
            ? "pr." + preSetup.PullRequestNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "main";
        var runId = Guid.NewGuid().ToString("N");
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var family in new[] { ("test", preSetup.TestEntries), ("build", preSetup.BuildEntries), ("publish", preSetup.PublishEntries) })
        {
            foreach (var entryId in family.Item2)
            {
                if (!preSetup.EntrySetupMap.TryGetValue(entryId, out var setup))
                {
                    continue;
                }

                var normalized = NormalizeKey(entryId);
                var osName = setup.RunnerOSSetup.Name.Replace("-", ".", StringComparison.Ordinal);
                var environment = (preSetup.Environment ?? string.Empty).Replace("-", ".", StringComparison.Ordinal);
                var invalidator = (setup.CacheInvalidator ?? "0").Replace("-", ".", StringComparison.Ordinal);
                var kind = runKind.Replace("-", ".", StringComparison.Ordinal);
                var id = runId.Replace("-", ".", StringComparison.Ordinal);
                var prefix = $"{family.Item1}.{osName}.{normalized}.{invalidator}.{environment}.{kind}.".Replace("..", ".", StringComparison.Ordinal);
                result[entryId] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["CONDITION"] = setup.Condition ? "true" : "false",
                    ["RUNS_ON"] = setup.RunnerOSSetup.RunnerPipelineOS,
                    ["RUN_SCRIPT"] = setup.RunnerOSSetup.RunScript,
                    ["CACHE_KEY"] = prefix + id,
                    ["CACHE_RESTORE_KEY"] = prefix + kind + ".",
                    ["CACHE_MAIN_RESTORE_KEY"] = prefix + "main.",
                    ["CHECKOUT_FETCH_DEPTH"] = setup.CheckoutFetchDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["CHECKOUT_FETCH_TAGS"] = setup.CheckoutFetchTags ? "true" : "false",
                    ["CHECKOUT_SUBMODULES"] = setup.CheckoutSubmodules switch
                    {
                        PlutoniumSubmoduleCheckoutType.SingleLevel => "true",
                        PlutoniumSubmoduleCheckoutType.Recursive => "recursive",
                        _ => "false",
                    },
                };
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the GitHub trigger block: env-branch pushes, <c>bump-*</c> tags, all pull requests.
    /// </summary>
    /// <param name="environmentBranches">The environment branches. Must not be null.</param>
    /// <returns>Opaque trigger YAML. Never null.</returns>
    public static string BuildGitHubTriggers(IReadOnlyList<string> environmentBranches)
    {
        ArgumentNullException.ThrowIfNull(environmentBranches);
        var branches = string.Join("\n", environmentBranches.Select(branch => "      - " + Quote(branch)));
        return "push:\n" +
            "    branches:\n" + branches + "\n" +
            "    tags:\n" +
            "      - 'bump-*'\n" +
            "pull_request:\n" +
            "    branches:\n" +
            "      - '**'";
    }

    /// <summary>
    /// Builds the GitHub concurrency block.
    /// </summary>
    /// <returns>Opaque concurrency YAML. Never null.</returns>
    public static string BuildGitHubConcurrency() =>
        "group: ${{ github.workflow }}-${{ github.ref }}\ncancel-in-progress: true";

    /// <summary>
    /// Builds the Azure trigger block mirroring the GitHub trigger set.
    /// </summary>
    /// <param name="environmentBranches">The environment branches. Must not be null.</param>
    /// <returns>Opaque trigger YAML. Never null.</returns>
    public static string BuildAzureTrigger(IReadOnlyList<string> environmentBranches)
    {
        ArgumentNullException.ThrowIfNull(environmentBranches);
        var branches = string.Join("\n", environmentBranches.Select(branch => "    - " + Quote(branch)));
        return "branches:\n" +
            "    include:\n" + branches + "\n" +
            "  tags:\n" +
            "    include:\n" +
            "      - 'bump-*'";
    }

    /// <summary>
    /// Builds the Azure pr block covering all branches.
    /// </summary>
    /// <returns>Opaque pr YAML. Never null.</returns>
    public static string BuildAzurePr() =>
        "branches:\n  include:\n    - '**'";

    /// <summary>
    /// Validates environment branches: lowercased, no duplicates, main present.
    /// </summary>
    /// <param name="environmentBranches">The environment branches. Must not be null.</param>
    /// <param name="mainEnvironmentBranch">The main branch. Must not be null or whitespace.</param>
    public static void CheckEnvironmentBranches(IEnumerable<string> environmentBranches, string mainEnvironmentBranch)
    {
        ArgumentNullException.ThrowIfNull(environmentBranches);
        ArgumentException.ThrowIfNullOrWhiteSpace(mainEnvironmentBranch);
        PlutoniumVersions.ValidateEnvironmentBranches(environmentBranches, mainEnvironmentBranch);
    }

    private static string NormalizeKey(string entryId) =>
        entryId.Trim().Replace("-", ".", StringComparison.Ordinal);

    private static string Quote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Contains('\'', StringComparison.Ordinal)
            ? "\"" + trimmed.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : "'" + trimmed + "'";
    }
}
