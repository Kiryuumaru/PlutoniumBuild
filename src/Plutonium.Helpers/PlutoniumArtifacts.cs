using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Packed artifacts passing entry outputs between pipeline jobs. Pack names the zip
/// <c>{stage}___{applower}___{ENTRYID}.zip</c> under
/// <c>{uploadRoot}/{applower}</c>: <c>01_pre_test</c> for tests flagged
/// <c>ExecuteBeforeBuild</c>, <c>03_post_test</c> for other tests,
/// <c>02_build</c> for builds, <c>04_publish</c> for publishes, always plus the
/// <c>$common</c> shared outputs. Unpack restores matching zips into
/// <c>{outputRoot}/{applower}/runtime</c>. Zipping itself stays Core-side via
/// <see cref="PlutoniumPaths"/>; only naming and selection live here.
/// </summary>
public static class PlutoniumArtifacts
{
    /// <summary>Gets the stage prefix for tests running before the build.</summary>
    public const string PreTestStage = "01_pre_test";

    /// <summary>Gets the stage prefix for builds.</summary>
    public const string BuildStage = "02_build";

    /// <summary>Gets the stage prefix for tests running after the build.</summary>
    public const string PostTestStage = "03_post_test";

    /// <summary>Gets the stage prefix for publishes.</summary>
    public const string PublishStage = "04_publish";

    /// <summary>
    /// Builds the artifact zip name for an entry and app: stage, lowercase AppId,
    /// and uppercase entry id joined by the layout separator.
    /// </summary>
    /// <param name="stage">The stage prefix. Must not be null or whitespace.</param>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="entryId">The entry id. Must not be null or whitespace.</param>
    /// <returns>The zip file name. Never null.</returns>
    public static string ArtifactZipName(string stage, PlutoniumAppId appId, string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        return stage.Trim()
            + PlutoniumOutputLayout.ArtifactNameSeparator
            + appId.Value.ToLowerInvariant()
            + PlutoniumOutputLayout.ArtifactNameSeparator
            + entryId.Trim().ToUpperInvariant()
            + ".zip";
    }

    /// <summary>
    /// Resolves the stage prefix for an entry, awaiting the test ordering flag.
    /// </summary>
    /// <param name="entry">The entry. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stage prefix. Never null.</returns>
    /// <exception cref="NotSupportedException">Thrown for unknown entry kinds.</exception>
    public static async Task<string> StageForAsync(IRunEntryDefinition entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        return entry switch
        {
            ITestEntryDefinition test => await test.GetExecuteBeforeBuildAsync().ConfigureAwait(false)
                ? PreTestStage
                : PostTestStage,
            IBuildEntryDefinition => BuildStage,
            IPublishEntryDefinition => PublishStage,
            _ => throw new NotSupportedException($"Entry '{entry.Id}' has an unsupported kind '{entry.GetType().Name}'."),
        };
    }

    /// <summary>
    /// Packs one application's runtime outputs into its artifact zip.
    /// Skips apps whose runtime directory is missing; returns the zip or null.
    /// </summary>
    /// <param name="appIdValue">The AppId value (or <c>$common</c>). Must not be null or whitespace.</param>
    /// <param name="stage">The stage prefix. Must not be null or whitespace.</param>
    /// <param name="entryId">The entry id. Must not be null or whitespace.</param>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <param name="uploadRoot">The artifact upload root. Must not be null.</param>
    /// <param name="logger">Optional logger for the created-artifact line. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The zip path, or null when no runtime outputs exist.</returns>
    public static async Task<PlutoniumPath?> PackAppAsync(
        string appIdValue,
        string stage,
        string entryId,
        PlutoniumPath commonOutputDirectory,
        PlutoniumPath uploadRoot,
        IPlutoniumLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appIdValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        ArgumentNullException.ThrowIfNull(uploadRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var lowered = appIdValue.Trim().ToLowerInvariant();
        var runtime = commonOutputDirectory.Combine(lowered, PlutoniumOutputLayout.RuntimeDirectoryName);
        if (!Directory.Exists(runtime.Value))
        {
            return null;
        }

        var zipName = stage.Trim()
            + PlutoniumOutputLayout.ArtifactNameSeparator
            + lowered
            + PlutoniumOutputLayout.ArtifactNameSeparator
            + entryId.Trim().ToUpperInvariant()
            + ".zip";
        var zip = uploadRoot.Combine(lowered, zipName);

        await Task.Run(() =>
        {
            if (File.Exists(zip.Value))
            {
                File.Delete(zip.Value);
            }

            PlutoniumPaths.PackZip(runtime, zip, overwrite: true);
        }, cancellationToken).ConfigureAwait(false);

        logger?.Log(PlutoniumLogLevel.Information, $"Created artifact '{zip}'.");
        return zip;
    }

    /// <summary>
    /// Packs an entry's outputs: every AppId plus the <c>$common</c> shared outputs.
    /// </summary>
    /// <param name="entry">The entry. Must not be null.</param>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <param name="uploadRoot">The artifact upload root. Must not be null.</param>
    /// <param name="logger">Optional logger. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created zips, possibly empty. Never null.</returns>
    public static async Task<IReadOnlyList<PlutoniumPath>> PackArtifactsAsync(
        IRunEntryDefinition entry,
        PlutoniumPath commonOutputDirectory,
        PlutoniumPath uploadRoot,
        IPlutoniumLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        ArgumentNullException.ThrowIfNull(uploadRoot);

        var stage = await StageForAsync(entry, cancellationToken).ConfigureAwait(false);
        var created = new List<PlutoniumPath>();
        foreach (var appId in entry.AppIds)
        {
            var packed = await PackAppAsync(
                appId.Value, stage, entry.Id, commonOutputDirectory, uploadRoot, logger, cancellationToken).ConfigureAwait(false);
            if (packed is not null)
            {
                created.Add(packed);
            }
        }

        var common = await PackAppAsync(
            PlutoniumOutputLayout.CommonAppId, stage, entry.Id, commonOutputDirectory, uploadRoot, logger, cancellationToken).ConfigureAwait(false);
        if (common is not null)
        {
            created.Add(common);
        }

        return created;
    }

    /// <summary>
    /// Unpacks artifact zips whose embedded AppId matches one of the given ids
    /// into <c>{outputRoot}/{applower}/runtime</c>. Null or empty ids unpack all.
    /// </summary>
    /// <param name="artifactsRoot">The artifact directory to scan. Must not be null.</param>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <param name="appIds">The AppId filter, or null to unpack all.</param>
    /// <param name="logger">Optional logger. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The restored runtime directories. Never null.</returns>
    public static async Task<IReadOnlyList<PlutoniumPath>> UnpackArtifactsAsync(
        PlutoniumPath artifactsRoot,
        PlutoniumPath commonOutputDirectory,
        IEnumerable<string>? appIds = null,
        IPlutoniumLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactsRoot);
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);

        var restored = new List<PlutoniumPath>();
        if (!Directory.Exists(artifactsRoot.Value))
        {
            return restored;
        }

        var filter = appIds is null
            ? null
            : new HashSet<string>(
                appIds.Select(id =>
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(appIds));
                    return id.Trim().ToLowerInvariant();
                }),
                StringComparer.Ordinal);

        var zips = Directory
            .EnumerateFiles(artifactsRoot.Value, "*.zip", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var zipValue in zips)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(zipValue);
            var segments = name.Split(PlutoniumOutputLayout.ArtifactNameSeparator, StringSplitOptions.None);
            if (segments.Length < 3)
            {
                continue;
            }

            var appLower = segments[1].ToLowerInvariant();
            if (filter is not null && !filter.Contains(appLower))
            {
                continue;
            }

            var destination = commonOutputDirectory.Combine(appLower, PlutoniumOutputLayout.RuntimeDirectoryName);
            var zip = new PlutoniumPath(zipValue);
            await Task.Run(
                () => PlutoniumPaths.UnpackZip(zip, destination, overwriteFiles: true),
                cancellationToken).ConfigureAwait(false);
            logger?.Log(PlutoniumLogLevel.Information, $"Unpacked artifact '{zip}' to '{destination}'.");
            restored.Add(destination);
        }

        return restored;
    }
}
