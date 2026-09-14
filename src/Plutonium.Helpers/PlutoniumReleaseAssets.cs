using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// One staged release asset: the staged path plus every computed digest.
/// </summary>
public sealed class PlutoniumReleaseAsset
{
    /// <summary>Gets the staged asset path. Never null.</summary>
    public PlutoniumPath Path { get; }

    /// <summary>Gets the asset file name. Never null.</summary>
    public string FileName { get; }

    /// <summary>Gets whether the source was a directory zipped before staging.</summary>
    public bool IsArchive { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumReleaseAsset"/> class.
    /// </summary>
    /// <param name="path">The staged path. Must not be null.</param>
    /// <param name="fileName">The file name. Must not be null or whitespace.</param>
    /// <param name="isArchive">Whether the source was a directory.</param>
    public PlutoniumReleaseAsset(PlutoniumPath path, string fileName, bool isArchive)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Path = path;
        FileName = fileName.Trim();
        IsArchive = isArchive;
    }
}

/// <summary>
/// One file digest: the algorithm display name plus the lowercase hex value.
/// </summary>
public sealed class PlutoniumFileDigest
{
    /// <summary>Gets the algorithm display name (for example <c>SHA-256</c>). Never null.</summary>
    public string AlgorithmName { get; }

    /// <summary>Gets the lowercase hex digest. Never null.</summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumFileDigest"/> class.
    /// </summary>
    /// <param name="algorithmName">The algorithm name. Must not be null or whitespace.</param>
    /// <param name="value">The digest. Must not be null or whitespace.</param>
    public PlutoniumFileDigest(string algorithmName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        AlgorithmName = algorithmName.Trim();
        Value = value.Trim();
    }
}

/// <summary>
/// Release assets: stages files (or zipped directories) under
/// <c>{outputRoot}/$common/runtime/assets</c> and computes the MD5, SHA-1,
/// SHA-256, and SHA-512 digests appended to release notes unless disabled.
/// Missing sources warn and stage nothing. Hashing stays Core-side via
/// <see cref="PlutoniumPaths"/>; only staging and digest selection live here.
/// </summary>
public static class PlutoniumReleaseAssets
{
    /// <summary>Gets the assets leaf under the shared runtime directory.</summary>
    public const string AssetsDirectoryName = "assets";

    /// <summary>
    /// Builds the assets directory: <c>{outputRoot}/$common/runtime/assets</c>.
    /// </summary>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <returns>The assets directory. Never null.</returns>
    public static PlutoniumPath AssetsDirectoryFor(PlutoniumPath commonOutputDirectory)
    {
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        return PlutoniumOutputLayout.CommonOutputDirectoryFor(commonOutputDirectory).Combine(AssetsDirectoryName);
    }

    /// <summary>
    /// Stages a file (copied) or directory (zipped) as a release asset.
    /// Missing sources warn through the logger and stage nothing.
    /// </summary>
    /// <param name="source">The source file or directory. Must not be null.</param>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <param name="customFileName">Optional file name override. Null or whitespace keeps the source name.</param>
    /// <param name="logger">Optional logger for staged/missing lines. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The staged asset, or null when the source is missing.</returns>
    public static async Task<PlutoniumReleaseAsset?> AddReleaseAssetAsync(
        PlutoniumPath source,
        PlutoniumPath commonOutputDirectory,
        string? customFileName = null,
        IPlutoniumLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var assets = AssetsDirectoryFor(commonOutputDirectory);
        PlutoniumPaths.EnsureDirectory(assets);

        if (File.Exists(source.Value))
        {
            var name = string.IsNullOrWhiteSpace(customFileName)
                ? Path.GetFileName(source.Value)
                : customFileName.Trim();
            var destination = assets.Combine(name);
            await Task.Run(
                () => File.Copy(source.Value, destination.Value, overwrite: true),
                cancellationToken).ConfigureAwait(false);
            logger?.Log(PlutoniumLogLevel.Information, $"Added file '{source}' as '{name}' to release assets.");
            return new PlutoniumReleaseAsset(destination, name, isArchive: false);
        }

        if (Directory.Exists(source.Value))
        {
            var baseName = string.IsNullOrWhiteSpace(customFileName)
                ? new DirectoryInfo(source.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name
                : customFileName.Trim();
            var name = baseName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? baseName : baseName + ".zip";
            var destination = assets.Combine(name);
            await Task.Run(() =>
            {
                if (File.Exists(destination.Value))
                {
                    File.Delete(destination.Value);
                }

                PlutoniumPaths.PackZip(source, destination, overwrite: true);
            }, cancellationToken).ConfigureAwait(false);
            logger?.Log(PlutoniumLogLevel.Information, $"Added archive '{source}' as '{name}' to release assets.");
            return new PlutoniumReleaseAsset(destination, name, isArchive: true);
        }

        logger?.Log(PlutoniumLogLevel.Warning, $"Asset '{source}' does not exist.");
        return null;
    }

    /// <summary>
    /// Computes the MD5, SHA-1, SHA-256, and SHA-512 digests of an asset file.
    /// </summary>
    /// <param name="asset">The asset file. Must not be null and must exist.</param>
    /// <returns>The four digests in algorithm order. Never null.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="asset"/> does not exist.</exception>
    public static IReadOnlyList<PlutoniumFileDigest> ComputeHashes(PlutoniumPath asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return
        [
            new PlutoniumFileDigest("MD5", PlutoniumPaths.ComputeMd5(asset)),
            new PlutoniumFileDigest("SHA-1", PlutoniumPaths.ComputeSha1(asset)),
            new PlutoniumFileDigest("SHA-256", PlutoniumPaths.ComputeSha256(asset)),
            new PlutoniumFileDigest("SHA-512", PlutoniumPaths.ComputeSha512(asset)),
        ];
    }

    /// <summary>
    /// Formats digests as release-notes lines: <c>{fileName} {ALGORITHM}: {digest}</c>.
    /// </summary>
    /// <param name="fileName">The asset file name. Must not be null or whitespace.</param>
    /// <param name="digests">The digests. Must not be null; entries must not be null.</param>
    /// <returns>The lines. Never null.</returns>
    public static IReadOnlyList<string> FormatHashLines(string fileName, IEnumerable<PlutoniumFileDigest> digests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(digests);

        var lines = new List<string>();
        foreach (var digest in digests)
        {
            ArgumentNullException.ThrowIfNull(digest, nameof(digests));
            lines.Add($"{fileName.Trim()} {digest.AlgorithmName}: {digest.Value}");
        }

        return lines;
    }

    /// <summary>
    /// Lists staged asset files under the assets directory, sorted. Missing roots yield empty.
    /// </summary>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <returns>The staged files. Never null.</returns>
    public static IReadOnlyList<PlutoniumPath> ListStagedAssets(PlutoniumPath commonOutputDirectory)
    {
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);

        var assets = AssetsDirectoryFor(commonOutputDirectory);
        if (!Directory.Exists(assets.Value))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(assets.Value, "*", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(value => new PlutoniumPath(value))
            .ToList();
    }
}
