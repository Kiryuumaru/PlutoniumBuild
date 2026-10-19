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
/// GitHub Actions pipeline: parses <c>GITHUB_REF</c> refs, transports the
/// pre-setup payload via <c>PLUTONIUM_PRE_SETUP</c>, and emits the workflow
/// document through the opaque Core GitHub emitter. Helpers owns refs, matrix,
/// and trigger text; Core owns YAML emission and host primitives.
/// </summary>
public sealed class PlutoniumGitHubPipeline : IPlutoniumPipeline
{
    /// <inheritdoc/>
    public PlutoniumPipelineType PipelineType => PlutoniumPipelineType.GitHub;

    /// <inheritdoc/>
    /// <remarks>
    /// Non-bump tags resolve their branch via <c>git branch -r --contains</c>;
    /// any resolution failure keeps the current-branch/main fallback.
    /// </remarks>
    public async Task<PlutoniumPipelineInfo> GetPipelineInfoAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var gitHubRef = Environment.GetEnvironmentVariable("GITHUB_REF");
        var baseRef = Environment.GetEnvironmentVariable("GITHUB_BASE_REF");
        var fallback = await FallbackBranchAsync(context, cancellationToken).ConfigureAwait(false);
        var containing = await ResolveContainingBranchAsync(context, gitHubRef, cancellationToken).ConfigureAwait(false);
        return PlutoniumPipelineDetection.ParseGitHubRef(gitHubRef, baseRef, fallback, containing);
    }

    /// <inheritdoc/>
    public Task<PlutoniumPipelinePreSetup> GetPipelinePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.PreSetup is not null)
        {
            return Task.FromResult(context.PreSetup);
        }

        return Task.FromResult(PlutoniumPipelinePreSetup.FromEnvironment());
    }

    /// <inheritdoc/>
    public Task PreparePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FinalizePreSetupAsync(PlutoniumPipelineContext context, PlutoniumPipelinePreSetup preSetup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preSetup);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = preSetup.ToPayload();
        context.Services.Host.ExportVariable(PlutoniumPipelineContract.PayloadVariableName, payload);
        context.Services.Host.ExportJobOutput(PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName), payload);
        foreach (var entryId in preSetup.TestEntries.Concat(preSetup.BuildEntries).Concat(preSetup.PublishEntries).Distinct(StringComparer.Ordinal))
        {
            var condition = preSetup.EntrySetupMap.TryGetValue(entryId, out var setup) && setup is not null && setup.Condition
                ? "true"
                : "false";
            context.Services.Host.ExportJobOutput(PlutoniumPipelineContract.ConditionOutputName(entryId), condition);
        }

        context.Services.Host.ExportJobOutput(PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID"), preSetup.BuildId ?? string.Empty);

        context.Services.Host.ExportJobOutput(PlutoniumPipelineContract.OutputName("PRE_SETUP", "HAS_RELEASE"), preSetup.HasRelease ? "true" : "false");

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task PrepareEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FinalizeEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task PreparePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await PlutoniumArtifacts.UnpackArtifactsAsync(
            context.Options.TemporaryDirectory,
            context.Options.CommonOutputDirectory,
            appIds: null,
            context.Services.Logger,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task FinalizePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Entry jobs render runs-on/run-script/checkout from the
    /// <c>EntrySetupMap</c> entry recorded by pre-setup when present; otherwise a
    /// lightweight config-only capture is attempted with no version reconcile.
    /// Entries the capture yields nothing for fall back to the Ubuntu2204 default.
    /// </remarks>
    public async Task<PlutoniumWorkflowBuildResult> BuildWorkflowAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        var name = await entries.WorkflowConfig.GetNameAsync().ConfigureAwait(false);
        var preOs = await entries.WorkflowConfig.GetPreSetupRunnerOSAsync().ConfigureAwait(false);
        var postOs = await entries.WorkflowConfig.GetPostSetupRunnerOSAsync().ConfigureAwait(false);
        var env = PlutoniumPipelineDetection.BuildWorkflowEnv(
            context.Build,
            "GITHUB_TOKEN",
            "${{ secrets.GITHUB_TOKEN }}",
            secretName => "${{ secrets." + secretName.Trim() + " }}");
        var triggers = PlutoniumPipelineDetection.BuildGitHubTriggers(context.Options.EnvironmentBranches);
        var concurrency = PlutoniumPipelineDetection.BuildGitHubConcurrency();
        var setups = context.PreSetup?.EntrySetupMap
            ?? await CaptureWorkflowSetupsAsync(entries, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        var jobs = await BuildJobsAsync(entries, preOs, postOs, setups).ConfigureAwait(false);
        var document = new PlutoniumWorkflowDocument(name, triggers, jobs, env);
        return new PlutoniumWorkflowBuildResult(document, concurrency, "contents: write");
    }

    private static async Task<string> FallbackBranchAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        try
        {
            var branch = await context.Services.Git.GetCurrentBranchAsync(context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(branch))
            {
                return branch.Trim();
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (TimeoutException)
        {
        }

        return context.Options.MainEnvironmentBranch;
    }

    /// <summary>
    /// Resolves the remote branch containing a non-bump tag via
    /// <c>git branch -r --contains</c>. Skips <c>origin/HEAD</c> lines, strips
    /// the <c>origin/</c> prefix, and returns the first remaining branch.
    /// Any process failure or empty output yields null so the caller keeps
    /// the current-branch/main fallback; cancellation still propagates.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="gitHubRef">The <c>GITHUB_REF</c> value. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The containing branch, or null when not a non-bump tag or unresolvable. May be null.</returns>
    private static async Task<string?> ResolveContainingBranchAsync(PlutoniumPipelineContext context, string? gitHubRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(gitHubRef))
        {
            return null;
        }

        var value = gitHubRef.Trim();
        if (!value.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("refs/tags/bump-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tag = value[10..].Trim();
        if (string.IsNullOrWhiteSpace(tag) ||
            tag.Contains('"', StringComparison.Ordinal) ||
            tag.Contains('\r', StringComparison.Ordinal) ||
            tag.Contains('\n', StringComparison.Ordinal))
        {
            return null;
        }

        PlutoniumProcessResult result;
        try
        {
            result = await context.Services.ProcessRunner.RunAsync(
                new PlutoniumProcessRequest("git", $"branch -r --contains \"{tag}\"", context.Options.RepositoryRoot.Value),
                context.Services.Masker,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }

        if (!result.Succeeded)
        {
            return null;
        }

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var branch = line.Trim();
            if (branch.Length == 0 || branch.Contains("->", StringComparison.Ordinal))
            {
                continue;
            }

            if (branch.StartsWith("origin/", StringComparison.Ordinal))
            {
                branch = branch["origin/".Length..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(branch))
            {
                return branch;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds staged jobs: PRE_SETUP, then pre-test (ExecuteBeforeBuild tests),
    /// build, post-test, and publish stages, then POST_SETUP on the post-setup OS.
    /// Entry jobs render runs-on/run-script/checkout from their setup entry;
    /// entries without a setup fall back to the Ubuntu2204 default.
    /// </summary>
    /// <remarks>
    /// Every entry job needs PRE_SETUP first plus every job of the closest prior
    /// non-empty stage, so step conditions on <c>needs.PRE_SETUP.outputs.*</c>
    /// always resolve. AppId-overlap-aware chaining is deferred.
    /// Every entry and post-setup job also imports the pre-setup payload job
    /// output as the <c>PLUTONIUM_PRE_SETUP</c> job environment variable.
    /// </remarks>
    /// <param name="entries">The discovered entries. Must not be null.</param>
    /// <param name="preOs">The pre-setup RunnerOS. Must not be null.</param>
    /// <param name="postOs">The post-setup RunnerOS. Must not be null.</param>
    /// <param name="setups">The per-entry setups keyed by entry id. Must not be null; entries without a setup use the Ubuntu2204 default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordered jobs. Never null.</returns>
    private static async Task<List<PlutoniumWorkflowJob>> BuildJobsAsync(
        PlutoniumAllEntries entries,
        PlutoniumRunnerOS preOs,
        PlutoniumRunnerOS postOs,
        IReadOnlyDictionary<string, PlutoniumEntrySetup> setups,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(preOs);
        ArgumentNullException.ThrowIfNull(postOs);
        ArgumentNullException.ThrowIfNull(setups);
        var checkout = PlutoniumGitHubEmitter.CheckoutStep("0", fetchTags: true, submodules: "recursive");
        var preSetup = new PlutoniumWorkflowJob(
            "PRE_SETUP",
            preOs.Name,
            [checkout, PlutoniumGitHubEmitter.RunStep("PreSetup", PlutoniumPipelineInvocation.ComposePreSetup(preOs.GetRunScript(PlutoniumPipelineType.GitHub)), stepId: "PRE_SETUP_RUN")],
            outputs: BuildPreSetupOutputs(entries));
        var jobs = new List<PlutoniumWorkflowJob> { preSetup };
        var stages = await PartitionStagesAsync(entries, cancellationToken).ConfigureAwait(false);
        var priorStage = new List<string> { preSetup.Id };
        foreach (var stage in stages)
        {
            var stageIds = new List<string>();
            foreach (var entry in stage)
            {
                var fallback = PlutoniumRunnerOS.Ubuntu2204.GetPipelineOS(PlutoniumPipelineType.GitHub);
                var runsOn = fallback.Name;
                var runScript = fallback.RunScript;
                var fetchDepth = "1";
                var fetchTags = false;
                var submodules = "false";
                if (setups.TryGetValue(entry.Id, out var setup) && setup is not null)
                {
                    runsOn = setup.RunnerOSSetup.Name;
                    runScript = setup.RunnerOSSetup.RunScript;
                    fetchDepth = setup.CheckoutFetchDepth.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    fetchTags = setup.CheckoutFetchTags;
                    submodules = setup.CheckoutSubmodules switch
                    {
                        PlutoniumSubmoduleCheckoutType.SingleLevel => "true",
                        PlutoniumSubmoduleCheckoutType.Recursive => "recursive",
                        _ => "false",
                    };
                }

                var steps = new List<string>
                {
                    PlutoniumGitHubEmitter.CheckoutStep(fetchDepth, fetchTags: fetchTags, submodules: submodules),
                    PlutoniumGitHubEmitter.RunStep(
                        entry.Id,
                        PlutoniumPipelineInvocation.ComposeEntryRun(runScript, entry.Id),
                        $"needs.PRE_SETUP.outputs.{PlutoniumPipelineContract.ConditionOutputName(entry.Id)} == 'true'"),
                };
                var needs = new List<string> { preSetup.Id };
                foreach (var prior in priorStage)
                {
                    if (!needs.Contains(prior, StringComparer.Ordinal))
                    {
                        needs.Add(prior);
                    }
                }

                var env = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PlutoniumPipelineContract.PayloadVariableName] = "${{ needs.PRE_SETUP.outputs." + PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName) + " }}",
                    [PlutoniumPipelineContract.EntryVariableName] = entry.Id,
                };
                jobs.Add(new PlutoniumWorkflowJob(entry.Id, runsOn, steps, needs: needs, env: env));
                stageIds.Add(entry.Id);
            }

            if (stageIds.Count > 0)
            {
                priorStage = stageIds;
            }
        }

        var postNeeds = new List<string> { preSetup.Id };
        foreach (var stage in stages)
        {
            foreach (var entry in stage)
            {
                postNeeds.Add(entry.Id);
            }
        }

        var payloadEnv = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlutoniumPipelineContract.PayloadVariableName] = "${{ needs.PRE_SETUP.outputs." + PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName) + " }}",
        };
        jobs.Add(new PlutoniumWorkflowJob(
            "POST_SETUP",
            postOs.Name,
            [checkout, PlutoniumGitHubEmitter.RunStep("PostSetup", PlutoniumPipelineInvocation.ComposePostSetup(postOs.GetRunScript(PlutoniumPipelineType.GitHub)))],
            needs: postNeeds,
            ifCondition: "success() || failure()",
            env: payloadEnv));
        return jobs;
    }

    /// <summary>
    /// Captures per-entry setups for workflow rendering without version reconcile:
    /// RunnerOS, cache, and checkout knobs only. Entries whose knobs need a run
    /// context yield nothing and fall back to the Ubuntu2204 default at render time.
    /// </summary>
    /// <param name="entries">The discovered entries. Must not be null.</param>
    /// <param name="root">The repository root for relativizing cache paths. Must not be null or whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured setups keyed by entry id. Never null.</returns>
    private static async Task<IReadOnlyDictionary<string, PlutoniumEntrySetup>> CaptureWorkflowSetupsAsync(
        PlutoniumAllEntries entries,
        string root,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var setups = new Dictionary<string, PlutoniumEntrySetup>(StringComparer.Ordinal);
        foreach (var entry in entries.RunEntries.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.RunContext is null)
            {
                continue;
            }

            PlutoniumRunnerOS runnerOS;
            PlutoniumPath[] cachePaths;
            string cacheInvalidator;
            int fetchDepth;
            bool fetchTags;
            PlutoniumSubmoduleCheckoutType submodules;
            bool condition;
            try
            {
                runnerOS = await entry.GetRunnerOSAsync().ConfigureAwait(false) ?? PlutoniumRunnerOS.Ubuntu2204;
                cachePaths = await entry.GetCachePathsAsync().ConfigureAwait(false);
                cacheInvalidator = await entry.GetCacheInvalidatorAsync().ConfigureAwait(false);
                fetchDepth = await entry.GetCheckoutFetchDepthAsync().ConfigureAwait(false);
                fetchTags = await entry.GetCheckoutFetchTagsAsync().ConfigureAwait(false);
                submodules = await entry.GetCheckoutSubmodulesAsync().ConfigureAwait(false);
                condition = await entry.GetConditionAsync().ConfigureAwait(false);
                var spec = runnerOS.GetPipelineOS(PlutoniumPipelineType.GitHub);
                var relative = cachePaths.Select(path =>
                {
                    var full = Path.GetFullPath(path.Value);
                    var rootFull = Path.GetFullPath(root);
                    if (!full.StartsWith(rootFull, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Invalid cache path.");
                    }

                    return full[rootFull.Length..];
                }).ToList();
                setups[entry.Id] = new PlutoniumEntrySetup
                {
                    Id = entry.Id,
                    Condition = condition,
                    RunnerOSSetup = new PlutoniumRunnerSetup(runnerOS.Name, spec.OsPayloadJson, spec.RunScript),
                    CacheInvalidator = cacheInvalidator,
                    CachePaths = relative,
                    CheckoutFetchDepth = fetchDepth,
                    CheckoutFetchTags = fetchTags,
                    CheckoutSubmodules = submodules,
                };
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (ArgumentException)
            {
                continue;
            }
        }

        return setups;
    }

    /// <summary>
    /// Builds the PRE_SETUP job outputs: the pre-setup payload, one live
    /// CONDITION output per run entry referencing the PRE_SETUP_RUN step id,
    /// plus the live BUILD_ID and HAS_RELEASE outputs from the same step.
    /// Static runs-on/run-script/checkout values stay baked at generation
    /// via the EntrySetupMap and are never emitted as outputs.
    /// </summary>
    /// <remarks>
    /// The payload output references the PRE_SETUP_RUN step output produced by
    /// <see cref="FinalizePreSetupAsync"/> via <c>ExportJobOutput</c>; entry and
    /// post-setup jobs import it as the <c>PLUTONIUM_PRE_SETUP</c> job environment
    /// variable. BUILD_ID is always exported, as an empty string when null.
    /// </remarks>
    /// <param name="entries">The discovered entries. Must not be null.</param>
    /// <returns>The outputs map. Never null.</returns>
    private static Dictionary<string, string> BuildPreSetupOutputs(PlutoniumAllEntries entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName)] = "${{ steps.PRE_SETUP_RUN.outputs." + PlutoniumPipelineContract.OutputName("PRE_SETUP", PlutoniumPipelineContract.PayloadVariableName) + " }}",
            [PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID")] = "${{ steps.PRE_SETUP_RUN.outputs." + PlutoniumPipelineContract.OutputName("PRE_SETUP", "BUILD_ID") + " }}",
            [PlutoniumPipelineContract.OutputName("PRE_SETUP", "HAS_RELEASE")] = "${{ steps.PRE_SETUP_RUN.outputs." + PlutoniumPipelineContract.OutputName("PRE_SETUP", "HAS_RELEASE") + " }}",
        };
        foreach (var entryId in entries.RunEntries.Keys)
        {
            var name = PlutoniumPipelineContract.ConditionOutputName(entryId);
            outputs[name] = "${{ steps.PRE_SETUP_RUN.outputs." + name + " }}";
        }

        return outputs;
    }

    /// <summary>
    /// Partitions run entries into ordered stages: pre-test, build, post-test, publish.
    /// </summary>
    /// <param name="entries">The discovered entries. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The four stages in order. Never null.</returns>
    private static async Task<List<List<IRunEntryDefinition>>> PartitionStagesAsync(
        PlutoniumAllEntries entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var preTests = new List<IRunEntryDefinition>();
        var postTests = new List<IRunEntryDefinition>();
        foreach (var test in entries.TestEntries.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await test.GetExecuteBeforeBuildAsync().ConfigureAwait(false))
            {
                preTests.Add(test);
            }
            else
            {
                postTests.Add(test);
            }
        }

        return
        [
            preTests,
            [.. entries.BuildEntries.Values],
            postTests,
            [.. entries.PublishEntries.Values],
        ];
    }
}
