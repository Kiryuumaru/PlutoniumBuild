using System.Text.Json;
using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Entry cache: persists <c>CachePath</c> directories across runs keyed by invalidator.
/// The index at <c>{cacheRoot}/entry_index</c> maps live path strings to snapshot
/// directories under <c>{cacheRoot}/entry</c>; snapshots hold a <c>currentLatest</c>
/// file or directory. Reads tolerate corrupt JSON; writes tolerate I/O failures so
/// caching never breaks a build. Paths travel through <see cref="PlutoniumPaths"/>
/// and <see cref="PlutoniumPath"/>; no entry, AppId, or tag knowledge lives here
/// beyond resolving the entry's own cache paths.
/// </summary>
public static class PlutoniumEntryCache
{
    private static readonly JsonSerializerOptions IndexOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Gets the entry snapshot directory name under the cache root.</summary>
    public const string EntryDirectoryName = "entry";

    /// <summary>Gets the cache index file name under the cache root.</summary>
    public const string EntryIndexFileName = "entry_index";

    /// <summary>Gets the snapshot leaf holding the latest cached payload.</summary>
    public const string CurrentLatestName = "currentLatest";

    /// <summary>
    /// Builds the entry snapshot directory: <c>{cacheRoot}/entry</c>.
    /// </summary>
    /// <param name="commonCacheDirectory">The shared cache root. Must not be null.</param>
    /// <returns>The snapshot directory. Never null.</returns>
    public static PlutoniumPath EntryCacheDirectoryFor(PlutoniumPath commonCacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(commonCacheDirectory);
        return commonCacheDirectory.Combine(EntryDirectoryName);
    }

    /// <summary>
    /// Builds the cache index file: <c>{cacheRoot}/entry_index</c>.
    /// </summary>
    /// <param name="commonCacheDirectory">The shared cache root. Must not be null.</param>
    /// <returns>The index file. Never null.</returns>
    public static PlutoniumPath EntryCacheIndexFor(PlutoniumPath commonCacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(commonCacheDirectory);
        return commonCacheDirectory.Combine(EntryIndexFileName);
    }

    /// <summary>
    /// Reads the cache index tolerantly: missing or corrupt files yield an empty map.
    /// </summary>
    /// <param name="commonCacheDirectory">The shared cache root. Must not be null.</param>
    /// <returns>The index mapping live path strings to snapshot directories. Never null.</returns>
    public static IReadOnlyDictionary<string, PlutoniumPath> GetCacheIndex(PlutoniumPath commonCacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(commonCacheDirectory);
        return ReadIndexFile(EntryCacheIndexFor(commonCacheDirectory));
    }

    /// <summary>
    /// Reads the cache index at an explicit file tolerantly.
    /// </summary>
    /// <param name="indexFile">The index file. Must not be null.</param>
    /// <returns>The index. Never null.</returns>
    public static IReadOnlyDictionary<string, PlutoniumPath> GetCacheIndexFromFile(PlutoniumPath indexFile)
    {
        ArgumentNullException.ThrowIfNull(indexFile);
        return ReadIndexFile(indexFile);
    }

    /// <summary>
    /// Writes the cache index, creating parents as needed. I/O failures are swallowed
    /// so caching never breaks a build; the failure is reported through the logger.
    /// </summary>
    /// <param name="commonCacheDirectory">The shared cache root. Must not be null.</param>
    /// <param name="index">The index. Must not be null.</param>
    /// <param name="logger">Optional logger for the swallowed failure. May be null.</param>
    public static void SetCacheIndex(
        PlutoniumPath commonCacheDirectory,
        IReadOnlyDictionary<string, PlutoniumPath> index,
        IPlutoniumLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(commonCacheDirectory);
        WriteIndexFile(EntryCacheIndexFor(commonCacheDirectory), index, logger);
    }

    /// <summary>
    /// Restores cached payloads before an entry runs: prunes orphan snapshot dirs,
    /// drops stale keys, then moves each <c>currentLatest</c> back into place.
    /// Move failures warn and continue so caching never breaks a build.
    /// </summary>
    /// <param name="entry">The entry. Must not be null and must carry a run context.</param>
    /// <param name="commonCacheDirectory">The shared cache root. Must not be null.</param>
    /// <param name="logger">Optional logger for cache hit/miss lines. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the preload.</returns>
    public static async Task CachePreloadAsync(
        IRunEntryDefinition entry,
        PlutoniumPath commonCacheDirectory,
        IPlutoniumLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(commonCacheDirectory);

        var snapshotRoot = EntryCacheDirectoryFor(commonCacheDirectory);
        PlutoniumPaths.EnsureDirectory(snapshotRoot);
        var indexFile = EntryCacheIndexFor(commonCacheDirectory);

        var cachePaths = await entry.GetCachePathsAsync().ConfigureAwait(false);
        var index = new Dictionary<string, PlutoniumPath>(
            ReadIndexFile(indexFile), StringComparer.Ordinal);

        PruneOrphans(snapshotRoot, index, logger);
        DropStaleKeys(index, cachePaths, logger);

        foreach (var path in cachePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!index.TryGetValue(path.Value, out var snapshot) ||
                !Directory.Exists(snapshot.Value))
            {
                logger?.Log(PlutoniumLogLevel.Information, $"Cache missed for '{path}'.");
                continue;
            }

            try
            {
                var latest = snapshot.Combine(CurrentLatestName);
                if (File.Exists(latest.Value))
                {
                    EnsureParent(path);
                    ReplaceFile(latest.Value, path.Value);
                    logger?.Log(PlutoniumLogLevel.Information, $"Cache loaded for '{path}'.");
                }
                else if (Directory.Exists(latest.Value))
                {
                    EnsureParent(path);
                    ReplaceDirectory(latest.Value, path.Value);
                    logger?.Log(PlutoniumLogLevel.Information, $"Cache loaded for '{path}'.");
                }
                else
                {
                    logger?.Log(PlutoniumLogLevel.Information, $"Cache missed for '{path}'.");
                }
            }
            catch (IOException ex)
            {
                logger?.Log(PlutoniumLogLevel.Warning, $"Cache load failed for '{path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.Log(PlutoniumLogLevel.Warning, $"Cache load failed for '{path}': {ex.Message}");
            }
        }

        WriteIndexFile(indexFile, index, logger);
    }

    /// <summary>
    /// Snapshots live payloads after an entry runs into <c>{snapshot}/currentLatest</c>,
    /// assigning fresh snapshot directories to new keys. Copy failures warn and continue
    /// so caching never breaks a build.
    /// </summary>
    /// <param name="entry">The entry. Must not be null and must carry a run context.</param>
    /// <param name="commonCacheDirectory">The shared cache root. Must not be null.</param>
    /// <param name="logger">Optional logger for cache save lines. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the postload.</returns>
    public static async Task CachePostloadAsync(
        IRunEntryDefinition entry,
        PlutoniumPath commonCacheDirectory,
        IPlutoniumLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(commonCacheDirectory);

        var snapshotRoot = EntryCacheDirectoryFor(commonCacheDirectory);
        PlutoniumPaths.EnsureDirectory(snapshotRoot);
        var indexFile = EntryCacheIndexFor(commonCacheDirectory);

        var cachePaths = await entry.GetCachePathsAsync().ConfigureAwait(false);
        var index = new Dictionary<string, PlutoniumPath>(
            ReadIndexFile(indexFile), StringComparer.Ordinal);

        foreach (var path in cachePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isFile = File.Exists(path.Value);
            var isDirectory = Directory.Exists(path.Value);
            if (!isFile && !isDirectory)
            {
                logger?.Log(PlutoniumLogLevel.Information, $"Cache missed for '{path}'.");
                continue;
            }

            if (!index.TryGetValue(path.Value, out var snapshot))
            {
                snapshot = snapshotRoot.Combine(Guid.NewGuid().ToString("N"));
                index[path.Value] = snapshot;
            }

            var latest = snapshot.Combine(CurrentLatestName);
            PlutoniumPaths.EnsureDirectory(snapshot);
            try
            {
                if (isFile)
                {
                    ReplaceFile(path.Value, latest.Value);
                }
                else
                {
                    ReplaceDirectory(path.Value, latest.Value);
                }
            }
            catch (IOException ex)
            {
                logger?.Log(PlutoniumLogLevel.Warning, $"Cache save failed for '{path}': {ex.Message}");
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.Log(PlutoniumLogLevel.Warning, $"Cache save failed for '{path}': {ex.Message}");
                continue;
            }

            logger?.Log(PlutoniumLogLevel.Information, $"Cache saved for '{path}'.");
        }

        WriteIndexFile(indexFile, index, logger);
    }

    private static IReadOnlyDictionary<string, PlutoniumPath> ReadIndexFile(PlutoniumPath indexFile)
    {
        if (!File.Exists(indexFile.Value))
        {
            return new Dictionary<string, PlutoniumPath>(StringComparer.Ordinal);
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(indexFile.Value), IndexOptions) ?? [];
            var index = new Dictionary<string, PlutoniumPath>(StringComparer.Ordinal);
            foreach (var pair in raw)
            {
                try
                {
                    index[pair.Key] = new PlutoniumPath(pair.Value);
                }
                catch (ArgumentException)
                {
                }
            }

            return index;
        }
        catch (IOException)
        {
            return new Dictionary<string, PlutoniumPath>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, PlutoniumPath>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, PlutoniumPath>(StringComparer.Ordinal);
        }
    }

    private static void WriteIndexFile(
        PlutoniumPath indexFile,
        IReadOnlyDictionary<string, PlutoniumPath> index,
        IPlutoniumLogger? logger)
    {
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(indexFile.Value));
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(
                indexFile.Value,
                JsonSerializer.Serialize(
                    index.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal),
                    IndexOptions));
        }
        catch (IOException ex)
        {
            logger?.Log(PlutoniumLogLevel.Warning, $"Cache index write failed for '{indexFile}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.Log(PlutoniumLogLevel.Warning, $"Cache index write failed for '{indexFile}': {ex.Message}");
        }
    }

    private static void PruneOrphans(
        PlutoniumPath snapshotRoot,
        Dictionary<string, PlutoniumPath> index,
        IPlutoniumLogger? logger)
    {
        if (!Directory.Exists(snapshotRoot.Value))
        {
            return;
        }

        var live = new HashSet<string>(
            index.Values.Select(snapshot => snapshot.Value), StringComparer.Ordinal);
        foreach (var directory in Directory.GetDirectories(snapshotRoot.Value))
        {
            if (!live.Contains(directory))
            {
                Directory.Delete(directory, recursive: true);
                logger?.Log(PlutoniumLogLevel.Information, $"Cache cleaned for '{directory}'.");
            }
        }
    }

    private static void DropStaleKeys(
        Dictionary<string, PlutoniumPath> index,
        PlutoniumPath[] cachePaths,
        IPlutoniumLogger? logger)
    {
        var live = new HashSet<string>(cachePaths.Select(path => path.Value), StringComparer.Ordinal);
        foreach (var key in index.Keys.ToList())
        {
            if (!live.Contains(key))
            {
                var snapshot = index[key];
                index.Remove(key);
                if (Directory.Exists(snapshot.Value))
                {
                    Directory.Delete(snapshot.Value, recursive: true);
                }

                logger?.Log(PlutoniumLogLevel.Information, $"Cache cleaned for '{key}'.");
            }
        }
    }

    private static void EnsureParent(PlutoniumPath path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path.Value));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static void ReplaceFile(string source, string destination)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Move(source, destination, overwrite: true);
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }
        else if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        Directory.Move(source, destination);
    }
}
