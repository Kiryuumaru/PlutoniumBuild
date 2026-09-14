using System.Text.Json;
using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Pre-setup orchestration: discovers entries, reconciles versions per app, builds the
/// run-type map and release notes, wires run contexts, captures per-entry setups, and
/// hands the payload to the pipeline for export. Post-setup publishes releases and
/// stamps version markers. One file owns both unlisted pipeline verbs.
/// </summary>
public static class PlutoniumPreSetup
{
    private static readonly JsonSerializerOptions ReleaseNotesOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Runs pre-setup: discovers entries, reconciles versions, builds the payload,
    /// wires run contexts and entry setups, then finalizes through the pipeline.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="run">The selected pipeline plus its info. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The computed payload. Never null.</returns>
    public static async Task<PlutoniumPipelinePreSetup> RunPreSetupAsync(
        PlutoniumPipelineContext context,
        PlutoniumPipelineRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(run);

        PlutoniumPipelineDetection.CheckEnvironmentBranches(context.Options.EnvironmentBranches, context.Options.MainEnvironmentBranch);
        var entries = await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        RequireApps(entries);

        PlutoniumSecrets.Setup(context.Build, context.Services.Logger, context.Services.Host.ExportVariable, context.Services.Masker);
        await run.Pipeline.PreparePreSetupAsync(context, cancellationToken).ConfigureAwait(false);

        var environment = run.Info.Branch.Trim().ToLowerInvariant();
        var useVersionFile = await entries.WorkflowConfig.GetUseJsonFileVersioningAsync().ConfigureAwait(false);
        var rawFile = useVersionFile ? PlutoniumVersionsFile.TryReadRaw(VersionsFilePath(context)) : null;
        var known = entries.AppEntryMap.Keys.ToList();
        var headCommit = await CurrentHeadCommitAsync(context, cancellationToken).ConfigureAwait(false);
        var remoteUrl = context.Options.RepositoryHttpsUrl;

        var apps = new Dictionary<string, (PlutoniumAppRunEntry Entry, PlutoniumRunType RunType, string ReleaseNotes)>(StringComparer.Ordinal);
        var releaseNotes = string.Empty;
        var buildId = await CurrentBuildIdAsync(context, cancellationToken).ConfigureAwait(false);
        var lastBuildId = string.Empty;
        var firstRelease = true;

        foreach (var key in entries.AppEntryMap.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appId = new PlutoniumAppId(entries.AppEntryMap[key].AppId);
            var allVersions = await PlutoniumVersions.GetAllVersionsAsync(
                context.Services.Git, appId, remoteUrl,
                context.Options.EnvironmentBranches, context.Options.MainEnvironmentBranch, headCommit,
                useVersionFile, rawFile, known, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);

            if (useVersionFile && run.Info.TriggerType != PlutoniumTriggerType.Tag)
            {
                PlutoniumVersionsFile.Verify(
                    allVersions.EnvVersionGrouped, allVersions.EnvLatestVersionPaired.Count == 0 ? new Dictionary<string, PlutoniumSemVersion>(StringComparer.Ordinal) : allVersions.EnvVersionFileMap,
                    appId.Value, [run.Info.Branch]);
            }

            var hasRelease = HasRelease(allVersions, environment, run.Info.TriggerType, useVersionFile);
            var latest = LatestVersion(allVersions, environment);
            var current = CurrentLatest(allVersions, environment);
            if (hasRelease)
            {
                UpdateLastBuildId(allVersions, environment, ref lastBuildId, ref firstRelease);
            }

            var stamped = StampVersion(latest, buildId, run.Info);
            apps[key] = (new PlutoniumAppRunEntry
            {
                AppId = appId.Value,
                Environment = environment,
                Version = stamped,
                OldVersion = current?.WithoutMetadata().ToString() ?? string.Empty,
                HasRelease = hasRelease,
            }, PlutoniumVersions.ToRunType(run.Info.TriggerType, hasRelease, useVersionFile), releaseNotes);
        }

        var withRelease = apps.Values.Where(app => app.Entry.HasRelease).ToList();
        if (withRelease.Count != 0)
        {
            releaseNotes = await PublishBumpTagsAsync(context, run, apps, buildId, environment, lastBuildId, firstRelease, cancellationToken).ConfigureAwait(false);
            foreach (var key in apps.Keys.ToList())
            {
                var app = apps[key];
                apps[key] = (app.Entry, app.RunType, releaseNotes);
            }
        }

        PlutoniumEntryRunner.SetupTargetRunContexts(
            entries.BuildEntries.Values.Cast<ITargetEntryDefinition>()
                .Concat(entries.PublishEntries.Values.Cast<ITargetEntryDefinition>()),
            apps, context.Options, run.PipelineType);
        PlutoniumEntryRunner.SetupDependentRunContexts(
            entries.TestEntries.Values.Cast<IDependentEntryDefinition>(),
            apps, context.Options, run.PipelineType);

        var payload = await BuildPayloadAsync(context, run, entries, apps, buildId, lastBuildId, cancellationToken).ConfigureAwait(false);
        context.PreSetup = payload;
        context.PipelineType = run.PipelineType;
        await run.Pipeline.FinalizePreSetupAsync(context, payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    /// <summary>
    /// Runs post-setup: unpacks shared artifacts, gathers staged release assets,
    /// uploads them to releases with hash-append notes, and stamps version markers.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="run">The selected pipeline plus its info. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the post-setup.</returns>
    /// <remarks>Local runs skip git tagging but still write version marker files.</remarks>
    public static async Task RunPostSetupAsync(
        PlutoniumPipelineContext context,
        PlutoniumPipelineRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(run);

        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        var payload = context.PreSetup ?? await run.Pipeline.GetPipelinePreSetupAsync(context, cancellationToken).ConfigureAwait(false);
        context.PreSetup = payload;

        await run.Pipeline.PreparePostSetupAsync(context, cancellationToken).ConfigureAwait(false);

        var appendHashes = await entries.WorkflowConfig.GetAppendReleaseNotesAssetHashesAsync().ConfigureAwait(false);
        var staged = PlutoniumReleaseAssets.ListStagedAssets(context.Options.CommonOutputDirectory);
        var notes = await BuildAssetNotesAsync(payload, staged, appendHashes, cancellationToken).ConfigureAwait(false);

        foreach (var app in payload.AppRunEntryMap.Values.Where(app => app.HasRelease))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StampReleaseAsync(context, app, notes, run.PipelineType, cancellationToken).ConfigureAwait(false);
        }

        if (run.PipelineType != PlutoniumPipelineType.Local && payload.HasRelease)
        {
            await PushTagsAsync(context, cancellationToken).ConfigureAwait(false);
            await UploadReleaseAssetsAsync(context, payload.BuildId, staged, cancellationToken).ConfigureAwait(false);
            await PublishDraftReleaseAsync(context, payload.BuildId, notes, cancellationToken).ConfigureAwait(false);
        }

        await run.Pipeline.FinalizePostSetupAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static void RequireApps(PlutoniumAllEntries entries)
    {
        if (entries.AppEntryMap.Count == 0)
        {
            throw new InvalidOperationException("No app entry configured.");
        }
    }

    private static string VersionsFilePath(PlutoniumPipelineContext context) =>
        context.Options.RepositoryRoot.Combine("versions.json").Value;

    private static async Task<string> CurrentHeadCommitAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        var result = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("git", "rev-parse HEAD", context.Options.RepositoryRoot.Value),
            context.Services.Masker, cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
        var commit = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(commit))
        {
            throw new InvalidOperationException("Failed to resolve HEAD commit.");
        }

        return commit;
    }

    private static async Task<string> CurrentBuildIdAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        var result = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("git", "show -s --date=format:%Y%m%d%H%M%S --format=%cd.%h --abbrev=7 HEAD", context.Options.RepositoryRoot.Value),
            context.Services.Masker, cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
        var buildId = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(buildId))
        {
            throw new InvalidOperationException("Failed to get build id from git.");
        }

        return buildId;
    }

    private static bool HasRelease(PlutoniumAllVersions allVersions, string environment, PlutoniumTriggerType trigger, bool useVersionFile)
    {
        if (!allVersions.EnvVersionGrouped.TryGetValue(environment, out var grouped) || grouped.Count == 0)
        {
            return false;
        }

        var last = grouped[^1];
        if (!allVersions.EnvLatestVersionPaired.TryGetValue(environment, out var current) || !current.Equals(last))
        {
            return allVersions.VersionBump.Contains(last) &&
                !allVersions.VersionQueue.Contains(last) &&
                !allVersions.VersionFailed.Contains(last) &&
                !allVersions.VersionPassed.Contains(last) &&
                ((useVersionFile && trigger == PlutoniumTriggerType.Commit) || trigger == PlutoniumTriggerType.Tag);
        }

        return false;
    }

    private static PlutoniumSemVersion? LatestVersion(PlutoniumAllVersions allVersions, string environment) =>
        allVersions.EnvVersionGrouped.TryGetValue(environment, out var grouped) && grouped.Count != 0 ? grouped[^1] : null;

    private static PlutoniumSemVersion? CurrentLatest(PlutoniumAllVersions allVersions, string environment) =>
        allVersions.EnvLatestVersionPaired.TryGetValue(environment, out var current) ? current : null;

    private static string StampVersion(PlutoniumSemVersion? latest, string buildId, PlutoniumPipelineInfo info)
    {
        PlutoniumSemVersion bare;
        if (latest is null)
        {
            bare = new PlutoniumSemVersion(0, 0, 0, info.Branch.Trim().ToLowerInvariant() + ".0");
        }
        else
        {
            bare = latest.WithoutMetadata();
        }

        return info.TriggerType == PlutoniumTriggerType.PullRequest
            ? new PlutoniumSemVersion(bare.Major, bare.Minor, bare.Patch, bare.Prerelease, $"build.{buildId}-pr.{info.PullRequestNumber}").ToString()
            : new PlutoniumSemVersion(bare.Major, bare.Minor, bare.Patch, bare.Prerelease, $"build.{buildId}").ToString();
    }

    /// <summary>
    /// Folds one environment's build ids into the shared floor: keeps the maximum
    /// build id within the environment, then keeps the minimum across apps so the
    /// release-notes start tag never skips an app's history. Skips environments
    /// with no grouped build ids. Clears <paramref name="firstRelease"/> when a
    /// build id is folded in.
    /// </summary>
    /// <param name="allVersions">The reconciled version taxonomy. Must not be null.</param>
    /// <param name="environment">The lowercase environment key.</param>
    /// <param name="lastBuildId">The running minimum build id floor; updated in place.</param>
    /// <param name="firstRelease">True until the first build id is folded in; updated in place.</param>
    private static void UpdateLastBuildId(PlutoniumAllVersions allVersions, string environment, ref string lastBuildId, ref bool firstRelease)
    {
        if (!allVersions.EnvBuildIdGrouped.TryGetValue(environment, out var grouped) || grouped.Count == 0)
        {
            return;
        }

        var best = grouped[0];
        for (var index = 1; index < grouped.Count; index++)
        {
            if (PlutoniumVersionTags.CompareBuildIds(grouped[index], best) > 0)
            {
                best = grouped[index];
            }
        }

        firstRelease = false;
        lastBuildId = string.IsNullOrEmpty(lastBuildId)
            ? best
            : PlutoniumVersionTags.CompareBuildIds(best, lastBuildId) < 0 ? best : lastBuildId;
    }

    private static async Task<string> PublishBumpTagsAsync(
        PlutoniumPipelineContext context,
        PlutoniumPipelineRun run,
        IReadOnlyDictionary<string, (PlutoniumAppRunEntry Entry, PlutoniumRunType RunType, string ReleaseNotes)> apps,
        string buildId,
        string environment,
        string lastBuildId,
        bool firstRelease,
        CancellationToken cancellationToken)
    {
        var logger = context.Services.Logger;
        var buildTag = PlutoniumVersionTags.BuildTag(buildId);
        var withRelease = apps.Values.Where(app => app.Entry.HasRelease).ToList();
        foreach (var app in withRelease)
        {
            logger.Log(PlutoniumLogLevel.Information, $"{app.Entry.AppId} on {app.Entry.Environment} has new version {app.Entry.Version}");
        }

        if (run.PipelineType == PlutoniumPipelineType.Local)
        {
            return string.Empty;
        }

        foreach (var app in withRelease)
        {
            var bare = PlutoniumSemVersion.Parse(app.Entry.Version).WithoutMetadata().ToString();
            await TagAsync(context, PlutoniumVersionTags.VersionTag(new PlutoniumAppId(app.Entry.AppId), PlutoniumSemVersion.Parse(bare), PlutoniumVersionTags.QueueSuffix), cancellationToken).ConfigureAwait(false);
            await TagAsync(context, PlutoniumVersionTags.VersionTag(new PlutoniumAppId(app.Entry.AppId), PlutoniumSemVersion.Parse(bare)), cancellationToken).ConfigureAwait(false);
        }

        await TagAsync(context, buildTag, cancellationToken).ConfigureAwait(false);
        await TagAsync(context, PlutoniumVersionTags.BuildTag(buildId, environment), cancellationToken).ConfigureAwait(false);
        await PushTagsAsync(context, cancellationToken).ConfigureAwait(false);

        var prerelease = await context.Entries!.WorkflowConfig.GetEnablePrereleaseOnReleaseAsync().ConfigureAwait(false) &&
            !environment.Equals(context.Options.MainEnvironmentBranch.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        var createArgs = $"\"{buildTag}\" --title \"{buildTag}\" --target \"{run.Info.Branch}\" --generate-notes --draft" +
            (prerelease ? " --prerelease" : string.Empty) +
            (!firstRelease && !string.IsNullOrEmpty(lastBuildId) ? $" --notes-start-tag \"{PlutoniumVersionTags.BuildTag(lastBuildId)}\"" : string.Empty);
        var created = await context.Services.Gh.CreateReleaseAsync(createArgs, context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        created.EnsureSucceeded();

        var viewed = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("gh", $"release view \"{buildTag}\" --json body", context.Options.RepositoryRoot.Value),
            context.Services.Masker, cancellationToken).ConfigureAwait(false);
        viewed.EnsureSucceeded();
        var notes = ReleaseBody(viewed.StandardOutput);

        var gitBase = context.Options.RepositoryHttpsUrl;
        var dot = gitBase.LastIndexOf('.');
        var slash = gitBase.LastIndexOf('/');
        if (dot > slash)
        {
            gitBase = gitBase[..dot];
        }

        var lines = string.Empty;
        foreach (var app in withRelease)
        {
            var id = app.Entry.AppId.ToLowerInvariant();
            var bare = PlutoniumSemVersion.Parse(app.Entry.Version).WithoutMetadata().ToString();
            lines += string.IsNullOrEmpty(app.Entry.OldVersion)
                ? $"\n* Bump `{id}` to `{bare}`. See [changelog]({gitBase}/commits/{id}/{bare})"
                : $"\n* Bump `{id}` from `{app.Entry.OldVersion}` to `{bare}`. See [changelog]({gitBase}/compare/{id}/{app.Entry.OldVersion}...{id}/{bare})";
        }

        lines = (withRelease.Count > 1 ? "## New Apps" : "## New Version") + lines + "\n\n" + notes;
        lines = lines.Replace("\n\n\n**Full Changelog**", "\n\n**Full Changelog**", StringComparison.Ordinal);
        logger.Log(PlutoniumLogLevel.Information, $"Generated release notes:\n{lines}");

        var notesPath = context.Options.TemporaryDirectory.Combine("notes.md");
        PlutoniumPaths.EnsureDirectory(context.Options.TemporaryDirectory);
        File.WriteAllText(notesPath.Value, lines);
        var edited = await context.Services.Gh.EditReleaseAsync($"\"{buildTag}\" --notes-file \"{notesPath.Value}\"", context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        edited.EnsureSucceeded();
        return lines;
    }

    private static string ReleaseBody(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.TryGetProperty("body", out var body) && body.GetString() is string notes)
            {
                return notes;
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Release notes document is empty.", ex);
        }

        throw new InvalidOperationException("Release notes document is empty.");
    }

    private static async Task TagAsync(PlutoniumPipelineContext context, string tag, CancellationToken cancellationToken)
    {
        var result = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("git", $"tag -f \"{tag}\"", context.Options.RepositoryRoot.Value),
            context.Services.Masker, cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
    }

    private static async Task PushTagsAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken)
    {
        var result = await context.Services.ProcessRunner.RunAsync(
            new PlutoniumProcessRequest("git", "push -f --tags", context.Options.RepositoryRoot.Value),
            context.Services.Masker, cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
    }

    private static async Task<PlutoniumPipelinePreSetup> BuildPayloadAsync(
        PlutoniumPipelineContext context,
        PlutoniumPipelineRun run,
        PlutoniumAllEntries entries,
        IReadOnlyDictionary<string, (PlutoniumAppRunEntry Entry, PlutoniumRunType RunType, string ReleaseNotes)> apps,
        string buildId,
        string lastBuildId,
        CancellationToken cancellationToken)
    {
        var root = context.Options.RepositoryRoot.Value;
        var entrySetups = new Dictionary<string, PlutoniumEntrySetup>(StringComparer.Ordinal);
        var tests = new List<string>();
        var builds = new List<string>();
        var publishes = new List<string>();

        foreach (var entry in entries.PublishEntries.Values)
        {
            entrySetups[entry.Id] = await CaptureSetupAsync(context, run, entry, root, cancellationToken).ConfigureAwait(false);
            publishes.Add(entry.Id);
        }

        foreach (var entry in entries.BuildEntries.Values)
        {
            entrySetups[entry.Id] = await CaptureSetupAsync(context, run, entry, root, cancellationToken).ConfigureAwait(false);
            builds.Add(entry.Id);
        }

        foreach (var entry in entries.TestEntries.Values)
        {
            entrySetups[entry.Id] = await CaptureSetupAsync(context, run, entry, root, cancellationToken).ConfigureAwait(false);
            tests.Add(entry.Id);
        }

        return new PlutoniumPipelinePreSetup
        {
            Branch = run.Info.Branch,
            TriggerType = run.Info.TriggerType,
            Environment = run.Info.Branch.Trim().ToLowerInvariant(),
            BuildId = buildId,
            LastBuildId = lastBuildId,
            PullRequestNumber = run.Info.PullRequestNumber,
            HasRelease = apps.Values.Any(app => app.Entry.HasRelease),
            TestEntries = tests,
            BuildEntries = builds,
            PublishEntries = publishes,
            EntrySetupMap = entrySetups,
            AppRunEntryMap = apps.ToDictionary(pair => pair.Key, pair => pair.Value.Entry, StringComparer.Ordinal),
        };
    }

    private static async Task<PlutoniumEntrySetup> CaptureSetupAsync(
        PlutoniumPipelineContext context,
        PlutoniumPipelineRun run,
        IRunEntryDefinition entry,
        string root,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cachePaths = await entry.GetCachePathsAsync().ConfigureAwait(false);
        var runnerOS = await entry.GetRunnerOSAsync().ConfigureAwait(false) ?? PlutoniumRunnerOS.Ubuntu2204;
        var spec = runnerOS.GetPipelineOS(run.PipelineType);
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

        return new PlutoniumEntrySetup
        {
            Id = entry.Id,
            RunTypes = entry.RunContext?.Apps.ToDictionary(
                pair => pair.Key.Value.ToLowerInvariant(), pair => pair.Value.RunType, StringComparer.Ordinal) ?? new Dictionary<string, PlutoniumRunType>(StringComparer.Ordinal),
            Condition = await entry.GetConditionAsync().ConfigureAwait(false),
            RunnerOSSetup = new PlutoniumRunnerSetup(runnerOS.Name, spec.OsPayloadJson, spec.RunScript),
            CacheInvalidator = await entry.GetCacheInvalidatorAsync().ConfigureAwait(false),
            CachePaths = relative,
            CheckoutFetchDepth = await entry.GetCheckoutFetchDepthAsync().ConfigureAwait(false),
            CheckoutFetchTags = await entry.GetCheckoutFetchTagsAsync().ConfigureAwait(false),
            CheckoutSubmodules = await entry.GetCheckoutSubmodulesAsync().ConfigureAwait(false),
        };
    }

    private static async Task<string> BuildAssetNotesAsync(
        PlutoniumPipelinePreSetup payload,
        IReadOnlyList<PlutoniumPath> staged,
        bool appendHashes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (staged.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var asset in staged)
        {
            var name = Path.GetFileName(asset.Value);
            lines.Add(name);
            if (appendHashes)
            {
                await Task.Run(() =>
                {
                    foreach (var line in PlutoniumReleaseAssets.FormatHashLines(name, PlutoniumReleaseAssets.ComputeHashes(asset)))
                    {
                        lines.Add(line);
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        return string.Join("\n", lines);
    }

    private static async Task StampReleaseAsync(
        PlutoniumPipelineContext context,
        PlutoniumAppRunEntry app,
        string assetNotes,
        PlutoniumPipelineType pipelineType,
        CancellationToken cancellationToken)
    {
        var version = PlutoniumSemVersion.Parse(app.Version).WithoutMetadata();
        var plain = PlutoniumVersionTags.VersionTag(new PlutoniumAppId(app.AppId), version);
        var notes = assetNotes.Length == 0 ? app.Version : app.Version + "\n" + assetNotes;
        context.Logger.Log(PlutoniumLogLevel.Information, $"Release {plain}: {notes}");
        PlutoniumPaths.EnsureDirectory(context.Options.TemporaryDirectory);
        await Task.Run(() =>
        {
            File.WriteAllText(
                context.Options.TemporaryDirectory.Combine(app.AppId.ToLowerInvariant() + ".release").Value,
                notes);
        }, cancellationToken).ConfigureAwait(false);
        if (pipelineType == PlutoniumPipelineType.Local)
        {
            return;
        }

        await TagAsync(context, plain, cancellationToken).ConfigureAwait(false);
        await TagAsync(context, plain + PlutoniumVersionTags.PassedSuffix, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UploadReleaseAssetsAsync(
        PlutoniumPipelineContext context,
        string buildId,
        IReadOnlyList<PlutoniumPath> staged,
        CancellationToken cancellationToken)
    {
        if (staged.Count == 0)
        {
            return;
        }

        var buildTag = PlutoniumVersionTags.BuildTag(buildId);
        foreach (var asset in staged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uploaded = await context.Services.Gh.UploadAssetAsync($"\"{buildTag}\" \"{asset.Value}\" --clobber", context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
            uploaded.EnsureSucceeded();
        }
    }

    private static async Task PublishDraftReleaseAsync(
        PlutoniumPipelineContext context,
        string buildId,
        string assetNotes,
        CancellationToken cancellationToken)
    {
        var buildTag = PlutoniumVersionTags.BuildTag(buildId);
        if (assetNotes.Length != 0)
        {
            var notesPath = context.Options.TemporaryDirectory.Combine("post-setup-notes.md");
            PlutoniumPaths.EnsureDirectory(context.Options.TemporaryDirectory);
            await Task.Run(() => File.WriteAllText(notesPath.Value, assetNotes), cancellationToken).ConfigureAwait(false);
            var noted = await context.Services.Gh.EditReleaseAsync($"\"{buildTag}\" --notes-file \"{notesPath.Value}\"", context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
            noted.EnsureSucceeded();
        }

        var published = await context.Services.Gh.EditReleaseAsync($"\"{buildTag}\" --draft=false", context.Options.RepositoryRoot.Value, cancellationToken).ConfigureAwait(false);
        published.EnsureSucceeded();
    }
}
