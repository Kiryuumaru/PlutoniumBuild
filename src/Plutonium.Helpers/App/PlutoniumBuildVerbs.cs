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
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;

namespace Plutonium.Helpers.App;

/// <summary>
/// Author CLI verbs plus the three unlisted pipeline verbs and the gated tag
/// maintenance verb. Wired as Core target definitions so the standalone runner
/// plans and executes them; default verb is <c>Interactive</c>. Helpers owns the
/// verb set and <c>--args</c> conventions; Core owns planning and execution.
/// </summary>
public static class PlutoniumBuildVerbs
{
    /// <summary>Gets the fetch verb name.</summary>
    public const string Fetch = "Fetch";

    /// <summary>Gets the version verb name.</summary>
    public const string Version = "Version";

    /// <summary>Gets the interactive verb name and default verb.</summary>
    public const string Interactive = "Interactive";

    /// <summary>Gets the bump verb name.</summary>
    public const string Bump = "Bump";

    /// <summary>Gets the bump-and-forget verb name.</summary>
    public const string BumpAndForget = "BumpAndForget";

    /// <summary>Gets the status-watch verb name.</summary>
    public const string StatusWatch = "StatusWatch";

    /// <summary>Gets the run verb name.</summary>
    public const string Run = "Run";

    /// <summary>Gets the test verb name.</summary>
    public const string Test = "Test";

    /// <summary>Gets the build verb name.</summary>
    public const string Build = "Build";

    /// <summary>Gets the publish verb name.</summary>
    public const string Publish = "Publish";

    /// <summary>Gets the GitHub workflow verb name.</summary>
    public const string GitHubWorkflow = "GithubWorkflow";

    /// <summary>Gets the Azure workflow verb name.</summary>
    public const string AzureWorkflow = "AzureWorkflow";

    /// <summary>Gets the unlisted pre-setup verb name.</summary>
    public const string PipelinePreSetup = "PipelinePreSetup";

    /// <summary>Gets the unlisted post-setup verb name.</summary>
    public const string PipelinePostSetup = "PipelinePostSetup";

    /// <summary>Gets the unlisted entry-run verb name.</summary>
    public const string PipelineEntryRun = "PipelineEntryRun";

    /// <summary>Gets the gated tag-deletion verb name.</summary>
    public const string DeleteOriginTags = "DeleteOriginTags";

    /// <summary>
    /// Builds every verb as a Core target definition bound to the given context.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <returns>The target definitions in menu order. Never null.</returns>
    public static IReadOnlyList<PlutoniumTargetDefinition> BuildTargets(PlutoniumPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            PlutoniumTargetDefinition.Define(Fetch).DescribedAs("Fetch commits and tags from origin.").ExecutesAsync(ct => RunFetchAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(Version).DescribedAs("Fetch, reconcile tags, print per-app version tables.").DependsOn(Fetch).ExecutesAsync(ct => RunVersionAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(Interactive).DescribedAs("Interactive picker over runnable tasks (default verb).").ExecutesAsync(ct => PlutoniumInteractiveConsole.RunAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(Bump).DescribedAs("Bump versions with --args {appid=[major|minor|patch|prerelease][>|+]{n}}, then watch.").DependsOn(Version).ExecutesAsync(ct => RunBumpAsync(context, watch: true, ct)).Build(),
            PlutoniumTargetDefinition.Define(BumpAndForget).DescribedAs("Bump versions without watching.").DependsOn(Version).ExecutesAsync(ct => RunBumpAsync(context, watch: false, ct)).Build(),
            PlutoniumTargetDefinition.Define(StatusWatch).DescribedAs("Attach to in-flight runs for the current commit.").ExecutesAsync(ct => RunStatusWatchAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(Run).DescribedAs("Run all local entries in DAG order.").ExecutesAsync(ct => PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Test, PlutoniumEntryType.Build, PlutoniumEntryType.Publish], ct)).Build(),
            PlutoniumTargetDefinition.Define(Test).DescribedAs("Run local test entries.").ExecutesAsync(ct => PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Test], ct)).Build(),
            PlutoniumTargetDefinition.Define(Build).DescribedAs("Run local build entries.").ExecutesAsync(ct => PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Build], ct)).Build(),
            PlutoniumTargetDefinition.Define(Publish).DescribedAs("Run local publish entries.").ExecutesAsync(ct => PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Publish], ct)).Build(),
            PlutoniumTargetDefinition.Define(GitHubWorkflow).DescribedAs("Regenerate the GitHub workflow file.").ExecutesAsync(ct => RunGitHubWorkflowAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(AzureWorkflow).DescribedAs("Regenerate the Azure workflow file.").ExecutesAsync(ct => RunAzureWorkflowAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(PipelinePreSetup).DescribedAs("Pipeline-internal pre-setup.").AsUnlisted().ExecutesAsync(ct => RunPipelinePreSetupAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(PipelinePostSetup).DescribedAs("Pipeline-internal post-setup.").AsUnlisted().ExecutesAsync(ct => RunPipelinePostSetupAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(PipelineEntryRun).DescribedAs("Pipeline-internal entry run.").AsUnlisted().ExecutesAsync(ct => RunPipelineEntryRunAsync(context, ct)).Build(),
            PlutoniumTargetDefinition.Define(DeleteOriginTags).DescribedAs("Delete origin tags for --args apps only; no-args wipe requires PLUTONIUM_ALLOW_TAG_WIPE=true.").AsUnlisted().ExecutesAsync(ct => RunDeleteOriginTagsAsync(context, ct)).Build(),
        ];
    }

    /// <summary>
    /// Resolves the requested verb, defaulting to <c>Interactive</c> when none is given.
    /// </summary>
    /// <param name="requested">The requested target names. Must not be null.</param>
    /// <returns>The effective targets. Never null.</returns>
    public static IReadOnlyList<string> DefaultWhenEmpty(IReadOnlyList<string> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        return requested.Count == 0 ? [Interactive] : requested;
    }

    /// <summary>
    /// Fetches commits and tags from origin.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the fetch.</returns>
    public static async Task RunFetchAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("git", "fetch origin --tags --force", context.Options.RepositoryRoot.Value),
            context.Services.Masker,
            cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
    }

    /// <summary>
    /// Reconciles remote tags per app and prints one version table per app.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the version report.</returns>
    public static async Task RunVersionAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        var head = await HeadCommitAsync(context, cancellationToken).ConfigureAwait(false);
        var known = entries.AppEntryMap.Keys.ToList();
        var useVersionFile = await entries.WorkflowConfig.GetUseJsonFileVersioningAsync().ConfigureAwait(false);
        var rawFile = useVersionFile ? PlutoniumVersionsFile.TryReadRaw(VersionsFilePath(context)) : null;
        var splitArgs = NormalizedSplitArgs(context);
        foreach (var key in known)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (splitArgs.Count != 0 && !splitArgs.ContainsKey(key))
            {
                continue;
            }

            var all = await PlutoniumVersions.GetAllVersionsAsync(
                context.Services.Git,
                new PlutoniumAppId(entries.AppEntryMap[key].AppId),
                context.Options.RepositoryHttpsUrl,
                context.Options.EnvironmentBranches,
                context.Options.MainEnvironmentBranch,
                head,
                useVersionFile,
                rawFile,
                known,
                workingDirectory: context.Options.RepositoryRoot.Value,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (useVersionFile)
            {
                PlutoniumVersionsFile.Verify(
                    all.EnvVersionGrouped, all.EnvVersionFileMap, key, context.Options.EnvironmentBranches);
            }

            PlutoniumInteractiveConsole.WriteVersionTable(context.Services.Logger, key, all);
        }
    }

    /// <summary>
    /// Bumps per-app versions from <c>--args</c> (or the interactive picker when
    /// absent), tags the bumped versions, pushes HEAD and the bump tags, force-moves
    /// the branch trigger tag, and optionally watches.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="watch">True to watch after bumping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the bump.</returns>
    public static async Task RunBumpAsync(PlutoniumPipelineContext context, bool watch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        if (await entries.WorkflowConfig.GetUseJsonFileVersioningAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("Bump is disabled if UseJsonFileVersioning is enabled.");
        }

        var known = entries.AppEntryMap.Keys.ToList();
        var argsMap = context.SplitArgs.Count == 0
            ? await PlutoniumInteractiveConsole.PromptBumpArgsAsync(context, known, cancellationToken).ConfigureAwait(false)
            : context.SplitArgs;
        var bumps = PlutoniumBumpArgs.ParseMap(argsMap, known);
        var branch = await context.Services.Git.GetCurrentBranchAsync(context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        var head = await HeadCommitAsync(context, cancellationToken).ConfigureAwait(false);
        var environment = branch.Trim().ToLowerInvariant();
        var tags = new List<string>();
        foreach (var pair in bumps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var all = await PlutoniumVersions.GetAllVersionsAsync(
                context.Services.Git,
                new PlutoniumAppId(entries.AppEntryMap[pair.Key].AppId),
                context.Options.RepositoryHttpsUrl,
                context.Options.EnvironmentBranches,
                context.Options.MainEnvironmentBranch,
                head,
                workingDirectory: context.Options.RepositoryRoot.Value,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var latest = all.EnvVersionGrouped.TryGetValue(environment, out var grouped) && grouped.Count != 0
                ? grouped[^1]
                : new PlutoniumSemVersion(0, 0, 0, environment + ".0");
            if (all.VersionCommitPaired.TryGetValue(latest, out var latestCommit) &&
                latestCommit.Equals(head, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Commit has already bumped {pair.Key}.");
            }

            var next = PlutoniumSemVersionBumps.ApplyBumps(latest.WithoutMetadata().Clone(), pair.Value);
            var candidate = next.WithoutMetadata().ToString();
            PlutoniumVersions.ValidateBumpVersion(all, candidate, branch, context.Options.MainEnvironmentBranch);
            tags.Add(PlutoniumVersionTags.VersionTag(new PlutoniumAppId(pair.Key), PlutoniumSemVersion.Parse(candidate), PlutoniumVersionTags.BumpSuffix));
        }

        foreach (var tag in tags)
        {
            await TagAsync(context, tag, force: false, cancellationToken).ConfigureAwait(false);
        }

        var headPush = await context.Services.Git.PushAsync("origin", "HEAD", context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        headPush.EnsureSucceeded();
        foreach (var tag in tags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pushed = await context.Services.Git.PushAsync(
                "origin", "refs/tags/" + tag, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
            pushed.EnsureSucceeded();
        }

        var bumpTag = PlutoniumVersionTags.BumpTriggerTag(branch);
        await context.Services.Git.PushAsync(
            "origin", ":refs/tags/" + bumpTag, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        await TagAsync(context, bumpTag, force: true, cancellationToken).ConfigureAwait(false);
        var triggerPushed = await context.Services.Git.PushAsync(
            "origin", "+refs/tags/" + bumpTag, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        triggerPushed.EnsureSucceeded();

        if (watch)
        {
            await RunStatusWatchCoreAsync(
                context,
                entries,
                branch,
                bumps.Keys.Select(appId => (appId, environment)).ToList(),
                cancelOnDone: true,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Attaches to in-flight runs for the current commit, repainting the watch table until canceled.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the watch.</returns>
    public static async Task RunStatusWatchAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        var branch = await SafeBranchAsync(context, cancellationToken).ConfigureAwait(false);
        context.Services.Logger.Log(PlutoniumLogLevel.Information, $"Watching branch '{branch}'.");
        await RunStatusWatchCoreAsync(
            context, entries, branch, [], cancelOnDone: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Polls reconciled tag state for every app until canceled, or until the
    /// requested targets settle or fail when <paramref name="cancelOnDone"/> is true.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="entries">The discovered entries. Must not be null.</param>
    /// <param name="branch">The branch being watched. Must not be null or whitespace.</param>
    /// <param name="targets">The lowercase app and environment pairs that settle the watch. Must not be null.</param>
    /// <param name="cancelOnDone">True to stop once the targets settle or fail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the watch.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a watched target reports a failed run.</exception>
    private static async Task RunStatusWatchCoreAsync(
        PlutoniumPipelineContext context,
        PlutoniumAllEntries entries,
        string branch,
        IReadOnlyList<(string AppId, string Environment)> targets,
        bool cancelOnDone,
        CancellationToken cancellationToken)
    {
        var useVersionFile = await entries.WorkflowConfig.GetUseJsonFileVersioningAsync().ConfigureAwait(false);
        var rawFile = useVersionFile ? PlutoniumVersionsFile.TryReadRaw(VersionsFilePath(context)) : null;
        var known = entries.AppEntryMap.Keys.ToList();
        var head = await HeadCommitAsync(context, cancellationToken).ConfigureAwait(false);
        var tick = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allDone = true;
            var pullFailed = false;
            var rows = new List<(string AppId, string Environment, string Version, string Status)>();
            var passed = new HashSet<(string AppId, string Environment)>();
            var failed = new HashSet<(string AppId, string Environment)>();
            foreach (var key in entries.AppEntryMap.Keys)
            {
                PlutoniumAllVersions all;
                try
                {
                    all = await PlutoniumVersions.GetAllVersionsAsync(
                        context.Services.Git,
                        new PlutoniumAppId(entries.AppEntryMap[key].AppId),
                        context.Options.RepositoryHttpsUrl,
                        context.Options.EnvironmentBranches,
                        context.Options.MainEnvironmentBranch,
                        head,
                        useVersionFile,
                        rawFile,
                        known,
                        workingDirectory: context.Options.RepositoryRoot.Value,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    pullFailed = true;
                    break;
                }
                catch (TimeoutException)
                {
                    pullFailed = true;
                    break;
                }

                foreach (var environment in all.EnvSorted)
                {
                    if (!all.EnvVersionGrouped.TryGetValue(environment, out var grouped) || grouped.Count == 0)
                    {
                        allDone = false;
                        rows.Add((key, environment, "-", "Not published"));
                        continue;
                    }

                    var latest = grouped[^1];
                    string status;
                    if (all.VersionFailed.Contains(latest))
                    {
                        status = "Run Failed";
                        failed.Add((key.ToLowerInvariant(), environment.ToLowerInvariant()));
                    }
                    else if (all.VersionPassed.Contains(latest))
                    {
                        status = "Published";
                        passed.Add((key.ToLowerInvariant(), environment.ToLowerInvariant()));
                    }
                    else if (all.VersionQueue.Contains(latest))
                    {
                        status = "Publishing";
                        allDone = false;
                    }
                    else if (all.VersionBump.Contains(latest))
                    {
                        status = "Waiting for queue";
                        allDone = false;
                    }
                    else
                    {
                        status = "Not published";
                        allDone = false;
                    }

                    var version = PlutoniumSemVersion.IsNullOrEmpty(latest) ? "-" : latest.WithoutMetadata().ToString();
                    rows.Add((key, environment, version, status));
                }
            }

            PlutoniumInteractiveConsole.WriteWatchTable(context.Services.Logger, branch, tick);
            if (pullFailed)
            {
                context.Services.Logger.Log(PlutoniumLogLevel.Error, "Error: Connection problems");
            }
            else
            {
                foreach (var row in rows)
                {
                    context.Services.Logger.Log(
                        PlutoniumLogLevel.Information,
                        $"{row.AppId} | {row.Environment} | {row.Version} | {row.Status}");
                }
            }

            tick++;
            if (cancelOnDone)
            {
                if (targets.Count == 0 && allDone)
                {
                    break;
                }

                if (targets.Any(failed.Contains))
                {
                    throw new InvalidOperationException("Pipeline run has failed.");
                }

                if (targets.Count != 0 && targets.All(passed.Contains))
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Regenerates the GitHub workflow file under <c>.github/workflows</c>.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The written file. Never null.</returns>
    public static async Task<PlutoniumPath> RunGitHubWorkflowAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.PipelineType = PlutoniumPipelineType.GitHub;
        await GenerateVersionsFileIfEnabledAsync(context, cancellationToken).ConfigureAwait(false);
        var built = await new PlutoniumGitHubPipeline().BuildWorkflowAsync(context, cancellationToken).ConfigureAwait(false);
        var file = WorkflowFileSlug(built.Document.Name);
        var workflowsRoot = context.Options.RepositoryRoot.Combine(".github", "workflows");
        var target = Path.GetFullPath(Path.Combine(workflowsRoot.Value, file + ".yml"));
        var root = Path.GetFullPath(workflowsRoot.Value) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workflow path must stay under .github/workflows.");
        }

        return PlutoniumGitHubEmitter.WriteWorkflow(context.Options.RepositoryRoot, file, built.Document, built.FirstBlock, built.SecondBlock);
    }

    /// <summary>
    /// Converts a workflow name to a safe file slug: trimmed, lowercased, spaces
    /// replaced with hyphens, then restricted to letters, digits, and hyphens.
    /// </summary>
    /// <param name="name">The workflow name. Must not be null.</param>
    /// <returns>The safe slug. Never null or empty.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the name cannot form a safe slug.</exception>
    private static string WorkflowFileSlug(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var slug = name.Trim().ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
        var valid = slug.Length > 0 &&
            ((slug[0] >= 'a' && slug[0] <= 'z') || (slug[0] >= '0' && slug[0] <= '9')) &&
            slug.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-');
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Workflow name '{name}' is not a safe workflow file name. Use letters, digits, and hyphens; the name is lowercased and spaces become hyphens.");
        }

        return slug;
    }

    /// <summary>
    /// Regenerates <c>azure-pipelines.yml</c> under the repository root.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The written file. Never null.</returns>
    public static async Task<PlutoniumPath> RunAzureWorkflowAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.PipelineType = PlutoniumPipelineType.Azure;
        await GenerateVersionsFileIfEnabledAsync(context, cancellationToken).ConfigureAwait(false);
        var built = await new PlutoniumAzurePipeline().BuildWorkflowAsync(context, cancellationToken).ConfigureAwait(false);
        return PlutoniumAzureEmitter.WriteWorkflow(context.Options.RepositoryRoot, built.Document, built.FirstBlock, built.SecondBlock, built.ThirdBlock);
    }

    /// <summary>
    /// Runs the unlisted pipeline pre-setup for the detected host.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The computed payload. Never null.</returns>
    public static async Task<PlutoniumPipelinePreSetup> RunPipelinePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var run = await PlutoniumPipelineDetection.SetupPipelineAsync(context, cancellationToken).ConfigureAwait(false);
        return await PlutoniumPreSetup.RunPreSetupAsync(context, run, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the unlisted pipeline post-setup for the detected host.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the post-setup.</returns>
    public static async Task RunPipelinePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var run = await PlutoniumPipelineDetection.SetupPipelineAsync(context, cancellationToken).ConfigureAwait(false);
        await PlutoniumPreSetup.RunPostSetupAsync(context, run, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the unlisted pipeline entry-run for the entry named by
    /// <c>PLUTONIUM_ENTRY</c>: rebuilds the entry's run contexts from the
    /// pre-setup payload and executes the entry once. Pre-setup never runs here.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the entry run.</returns>
    /// <remarks>
    /// Invoked by generated CI entry jobs. Missing or malformed environment input
    /// and unknown entry ids log the precise diagnosis before failing; an entry
    /// whose condition is false skips with success. The payload does not carry
    /// release notes, so reconstructed bump contexts carry empty notes.
    /// </remarks>
    public static async Task RunPipelineEntryRunAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var logger = context.Services.Logger;
        var entryId = Environment.GetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName)?.Trim();
        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw DiagnosedFailure(
                logger,
                "PLUTONIUM_ENTRY is missing or empty. This verb is invoked by generated CI entry jobs; regenerate the workflow files.");
        }

        var payload = context.PreSetup;
        if (payload is null)
        {
            try
            {
                payload = PlutoniumPipelinePreSetup.FromEnvironment();
            }
            catch (Exception failure) when (failure is InvalidOperationException or FormatException or JsonException or ArgumentException)
            {
                throw DiagnosedFailure(
                    logger,
                    $"The PLUTONIUM_PRE_SETUP payload for entry '{entryId}' is missing or malformed. This verb is invoked by generated CI entry jobs; regenerate the workflow files.",
                    failure);
            }

            context.PreSetup = payload;
        }

        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        if (!entries.RunEntries.TryGetValue(entryId, out var entry))
        {
            throw DiagnosedFailure(
                logger,
                $"Entry id '{entryId}' is not a discovered run entry. This verb is invoked by generated CI entry jobs; regenerate the workflow files.");
        }

        if (!payload.EntrySetupMap.TryGetValue(entryId, out var entrySetup) || entrySetup is null)
        {
            throw DiagnosedFailure(
                logger,
                $"The pre-setup payload has no setup for entry '{entryId}'. This verb is invoked by generated CI entry jobs; regenerate the workflow files.");
        }

        var apps = new Dictionary<string, (PlutoniumAppRunEntry Entry, PlutoniumRunType RunType, string ReleaseNotes)>(StringComparer.Ordinal);
        foreach (var pair in entrySetup.RunTypes)
        {
            if (!payload.AppRunEntryMap.TryGetValue(pair.Key, out var app))
            {
                throw DiagnosedFailure(
                    logger,
                    $"The pre-setup payload has no app outcome for entry '{entryId}'. This verb is invoked by generated CI entry jobs; regenerate the workflow files.");
            }

            apps[pair.Key] = (app, pair.Value, string.Empty);
        }

        if (entry is ITargetEntryDefinition target)
        {
            PlutoniumEntryRunner.SetupTargetRunContexts([target], apps, context.Options, context.PipelineType, context.Services);
        }
        else if (entry is IDependentEntryDefinition dependent)
        {
            PlutoniumEntryRunner.SetupDependentRunContexts([dependent], apps, context.Options, context.PipelineType, context.Services);
        }
        else
        {
            throw DiagnosedFailure(
                logger,
                $"Entry id '{entryId}' has an unsupported entry kind. This verb is invoked by generated CI entry jobs; regenerate the workflow files.");
        }

        logger.Log(PlutoniumLogLevel.Information, $"Running pipeline entry '{entryId}'.");
        if (!await PlutoniumEntryRunner.RunEntryAsync(entry, context.Options, context.Services, cancellationToken).ConfigureAwait(false))
        {
            logger.Log(PlutoniumLogLevel.Information, $"Entry '{entryId}' skipped (condition false).");
        }
    }

    private static InvalidOperationException DiagnosedFailure(IPlutoniumLogger logger, string message, Exception? inner = null)
    {
        logger.Log(PlutoniumLogLevel.Error, message);
        return inner is null ? new InvalidOperationException(message) : new InvalidOperationException(message, inner);
    }

    /// <summary>
    /// Deletes origin tags for the <c>--args</c> apps only. A no-args full wipe is
    /// refused unless <c>PLUTONIUM_ALLOW_TAG_WIPE=true</c> is set.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the deletion.</returns>
    public static async Task RunDeleteOriginTagsAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SplitArgs.Count == 0)
        {
            var allowed = string.Equals(Environment.GetEnvironmentVariable("PLUTONIUM_ALLOW_TAG_WIPE"), "true", StringComparison.OrdinalIgnoreCase);
            if (!allowed)
            {
                throw new InvalidOperationException("Refusing no-args tag wipe. Pass --args apps or set PLUTONIUM_ALLOW_TAG_WIPE=true.");
            }
        }

        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        var splitArgs = NormalizedSplitArgs(context);
        var targets = splitArgs.Count == 0 ? entries.AppEntryMap.Keys.ToList() : splitArgs.Keys.ToList();
        var remoteTags = await ListRemoteTagNamesAsync(context, cancellationToken).ConfigureAwait(false);
        foreach (var app in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = app + "/";
            foreach (var tag in remoteTags)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pushed = await context.Services.Git.PushAsync("origin", ":refs/tags/" + tag, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
                pushed.EnsureSucceeded();
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> NormalizedSplitArgs(PlutoniumPipelineContext context)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in context.SplitArgs)
        {
            normalized[pair.Key.Trim().ToLowerInvariant()] = pair.Value;
        }

        return normalized;
    }

    private static async Task<IReadOnlyList<string>> ListRemoteTagNamesAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        const string refsTagsPrefix = "refs/tags/";
        const string peelSuffix = "^{}";
        var result = await context.Services.Git.ListRemoteTagsAsync(context.Options.RepositoryHttpsUrl, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var peel = line.IndexOf(refsTagsPrefix, StringComparison.Ordinal);
            if (peel < 0)
            {
                continue;
            }

            var tag = line[(peel + refsTagsPrefix.Length)..].Trim();
            if (tag.EndsWith(peelSuffix, StringComparison.Ordinal))
            {
                tag = tag[..^peelSuffix.Length].Trim();
            }

            if (tag.Length != 0)
            {
                tags.Add(tag);
            }
        }

        return tags.ToList();
    }

    private static async Task<string> HeadCommitAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        var result = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("git", "rev-parse HEAD", context.Options.RepositoryRoot.Value),
            context.Services.Masker,
            cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
        var commit = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(commit))
        {
            throw new InvalidOperationException("Failed to resolve HEAD commit.");
        }

        return commit;
    }

    private static async Task<string> SafeBranchAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Services.Git.GetCurrentBranchAsync(context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return context.Options.MainEnvironmentBranch;
        }
    }

    private static async Task GenerateVersionsFileIfEnabledAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        if (!await entries.WorkflowConfig.GetUseJsonFileVersioningAsync().ConfigureAwait(false))
        {
            return;
        }

        var head = await HeadCommitAsync(context, cancellationToken).ConfigureAwait(false);
        var known = entries.AppEntryMap.Keys.ToList();
        var rawFile = PlutoniumVersionsFile.TryReadRaw(VersionsFilePath(context));
        var latestByApp = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>>>(StringComparer.Ordinal);
        foreach (var key in known)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var all = await PlutoniumVersions.GetAllVersionsAsync(
                context.Services.Git,
                new PlutoniumAppId(entries.AppEntryMap[key].AppId),
                context.Options.RepositoryHttpsUrl,
                context.Options.EnvironmentBranches,
                context.Options.MainEnvironmentBranch,
                head,
                true,
                rawFile,
                known,
                workingDirectory: context.Options.RepositoryRoot.Value,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            latestByApp[key] = all.EnvVersionGrouped;
        }

        PlutoniumVersionsFile.WriteRaw(VersionsFilePath(context), PlutoniumVersionsFile.GenerateRaw(latestByApp));
    }

    private static string VersionsFilePath(PlutoniumPipelineContext context) =>
        context.Options.RepositoryRoot.Combine("versions.json").Value;

    private static async Task TagAsync(PlutoniumPipelineContext context, string tag, bool force, CancellationToken cancellationToken)
    {
        var arguments = force ? $"tag -f \"{tag}\"" : $"tag \"{tag}\"";
        var tagged = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("git", arguments, context.Options.RepositoryRoot.Value),
            context.Services.Masker,
            cancellationToken).ConfigureAwait(false);
        tagged.EnsureSucceeded();
    }
}
