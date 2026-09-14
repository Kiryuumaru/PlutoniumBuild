using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Entry execution: sets up run contexts from the pre-setup payload, runs conditions
/// and execute steps with cache preload/postload, and targets artifact pack/unpack.
/// One file owns the whole per-entry path for local and pipeline runs.
/// </summary>
public static class PlutoniumEntryRunner
{
    /// <summary>
    /// Sets run contexts on target entries: one <see cref="AppRunContext"/> per AppId
    /// built from the pre-setup payload, versions, and release notes.
    /// </summary>
    /// <param name="entries">The target entries. Must not be null.</param>
    /// <param name="apps">The per-app payload and run type. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="pipelineType">The pipeline type.</param>
    public static void SetupTargetRunContexts(
        IEnumerable<ITargetEntryDefinition> entries,
        IReadOnlyDictionary<string, (PlutoniumAppRunEntry Entry, PlutoniumRunType RunType, string ReleaseNotes)> apps,
        PlutoniumBuildOptions options,
        PlutoniumPipelineType pipelineType)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(options);
        foreach (var entry in entries)
        {
            SetupRunContext(entry, apps, options, pipelineType);
        }
    }

    /// <summary>
    /// Sets run contexts on dependent entries: one <see cref="AppRunContext"/> per AppId
    /// built from the pre-setup payload, versions, and release notes.
    /// </summary>
    /// <param name="entries">The dependent entries. Must not be null.</param>
    /// <param name="apps">The per-app payload and run type. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="pipelineType">The pipeline type.</param>
    public static void SetupDependentRunContexts(
        IEnumerable<IDependentEntryDefinition> entries,
        IReadOnlyDictionary<string, (PlutoniumAppRunEntry Entry, PlutoniumRunType RunType, string ReleaseNotes)> apps,
        PlutoniumBuildOptions options,
        PlutoniumPipelineType pipelineType)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(options);
        foreach (var entry in entries)
        {
            SetupRunContext(entry, apps, options, pipelineType);
        }
    }

    /// <summary>
    /// Runs one entry: evaluates the condition, skips silently when false, otherwise
    /// preloads the cache, executes steps, packs artifacts, and postloads the cache.
    /// </summary>
    /// <param name="entry">The entry. Must not be null and must carry a run context.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="services">The build services. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entry executed; false when the condition skipped it.</returns>
    public static async Task<bool> RunEntryAsync(
        IRunEntryDefinition entry,
        PlutoniumBuildOptions options,
        PlutoniumBuildServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        if (!await entry.GetConditionAsync().ConfigureAwait(false))
        {
            return false;
        }

        await PlutoniumEntryCache.CachePreloadAsync(entry, options.CommonCacheDirectory, services.Logger, cancellationToken).ConfigureAwait(false);
        try
        {
            await entry.ExecuteAsync().ConfigureAwait(false);
            await PlutoniumArtifacts.PackArtifactsAsync(
                entry, options.CommonOutputDirectory, options.TemporaryDirectory, services.Logger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await PlutoniumEntryCache.CachePostloadAsync(entry, options.CommonCacheDirectory, services.Logger, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Runs entries in order, honoring the condition skip.
    /// </summary>
    /// <param name="entries">The entries. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="services">The build services. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The executed entries. Never null.</returns>
    public static async Task<IReadOnlyList<IRunEntryDefinition>> RunEntriesAsync(
        IEnumerable<IRunEntryDefinition> entries,
        PlutoniumBuildOptions options,
        PlutoniumBuildServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        var executed = new List<IRunEntryDefinition>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await RunEntryAsync(entry, options, services, cancellationToken).ConfigureAwait(false))
            {
                executed.Add(entry);
            }
        }

        return executed;
    }

    private static void SetupRunContext(
        IRunEntryDefinition entry,
        IReadOnlyDictionary<string, (PlutoniumAppRunEntry Entry, PlutoniumRunType RunType, string ReleaseNotes)> apps,
        PlutoniumBuildOptions options,
        PlutoniumPipelineType pipelineType)
    {
        var contexts = new Dictionary<PlutoniumAppId, AppRunContext>();
        foreach (var appId in entry.AppIds)
        {
            var key = appId.Value.ToLowerInvariant();
            if (!apps.TryGetValue(key, out var app))
            {
                continue;
            }

            var appVersion = new PlutoniumAppVersion(
                appId,
                app.Entry.Environment,
                string.IsNullOrWhiteSpace(app.Entry.Version) ? "0.0.0" : app.Entry.Version,
                string.IsNullOrWhiteSpace(app.Entry.Version) ? "0" : app.Entry.Version);
            PlutoniumBumpReleaseVersion? bump = null;
            PlutoniumPullRequestReleaseVersion? pull = null;
            if ((app.RunType & PlutoniumRunType.Bump) != 0)
            {
                bump = new PlutoniumBumpReleaseVersion(
                    appId, app.Entry.Environment, appVersion.Version, appVersion.BuildId, app.ReleaseNotes);
            }
            else if ((app.RunType & PlutoniumRunType.PullRequest) != 0)
            {
                pull = new PlutoniumPullRequestReleaseVersion(
                    appId, app.Entry.Environment, appVersion.Version, appVersion.BuildId, 0);
            }

            contexts[appId] = new AppRunContext(
                appId, app.RunType, options.CommonOutputDirectory, appVersion, bump, pull);
        }

        entry.RunContext = new PlutoniumRunContext(pipelineType, contexts);
    }
}
