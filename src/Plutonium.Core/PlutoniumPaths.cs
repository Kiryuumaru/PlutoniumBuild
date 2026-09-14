using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Plutonium.Core;

/// <summary>
/// Hash algorithms available for file hashing.
/// Covers the digest set release notes append (MD5, SHA-1, SHA-256, SHA-512).
/// </summary>
public enum PlutoniumHashAlgorithm
{
    /// <summary>MD5 digest.</summary>
    Md5 = 0,

    /// <summary>SHA-1 digest.</summary>
    Sha1 = 1,

    /// <summary>SHA-256 digest.</summary>
    Sha256 = 2,

    /// <summary>SHA-512 digest.</summary>
    Sha512 = 3,
}

/// <summary>
/// Static filesystem helpers: glob enumeration, directory create/copy,
/// zip pack/unpack, JSON read/write, file hashing, and temp-directory scopes.
/// Thin piecemeal surface for what entries need; no entry, AppId, or tag knowledge.
/// </summary>
public static class PlutoniumPaths
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Enumerates files in a directory matching any of the given search patterns.
    /// </summary>
    /// <param name="directory">The directory to search. Must not be null.</param>
    /// <param name="patterns">Search patterns (<c>*</c> and <c>?</c> wildcards). Must not be null; each entry must not be null or whitespace.</param>
    /// <param name="recursive">Whether to search subdirectories.</param>
    /// <returns>Matching files as paths, deduplicated and sorted. Never null.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="directory"/> does not exist.</exception>
    public static IReadOnlyList<PlutoniumPath> EnumerateFiles(
        PlutoniumPath directory,
        IEnumerable<string> patterns,
        bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(patterns);

        var directoryValue = directory.Value;
        if (!System.IO.Directory.Exists(directoryValue))
        {
            throw new DirectoryNotFoundException($"Directory '{directoryValue}' does not exist.");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var collected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern, nameof(patterns));
            foreach (var file in System.IO.Directory.EnumerateFiles(directoryValue, pattern.Trim(), searchOption))
            {
                collected.Add(file);
            }
        }

        return collected.Order(StringComparer.Ordinal).Select(path => new PlutoniumPath(path)).ToList();
    }

    /// <summary>
    /// Ensures a directory exists, creating it and any missing parents.
    /// </summary>
    /// <param name="directory">The directory. Must not be null.</param>
    /// <returns>The directory. Never null.</returns>
    public static PlutoniumPath EnsureDirectory(PlutoniumPath directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        System.IO.Directory.CreateDirectory(directory.Value);
        return directory;
    }

    /// <summary>
    /// Copies a directory tree to a destination, creating parents as needed.
    /// </summary>
    /// <param name="source">The source directory. Must not be null and must exist.</param>
    /// <param name="destination">The destination directory. Must not be null.</param>
    /// <param name="overwrite">Whether to overwrite existing files.</param>
    /// <returns>The destination directory. Never null.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="source"/> does not exist.</exception>
    public static PlutoniumPath CopyDirectory(PlutoniumPath source, PlutoniumPath destination, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (!System.IO.Directory.Exists(source.Value))
        {
            throw new DirectoryNotFoundException($"Source directory '{source.Value}' does not exist.");
        }

        System.IO.Directory.CreateDirectory(destination.Value);
        foreach (var directory in System.IO.Directory.GetDirectories(source.Value, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source.Value, directory);
            System.IO.Directory.CreateDirectory(Path.Combine(destination.Value, relative));
        }

        foreach (var file in System.IO.Directory.GetFiles(source.Value, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source.Value, file);
            var target = Path.Combine(destination.Value, relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }

        return destination;
    }

    /// <summary>
    /// Packs a directory into a zip archive.
    /// </summary>
    /// <param name="sourceDirectory">The directory to pack. Must not be null and must exist.</param>
    /// <param name="zipFile">The zip file to create. Must not be null.</param>
    /// <param name="overwrite">Whether to replace an existing zip.</param>
    /// <returns>The zip file path. Never null.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="sourceDirectory"/> does not exist.</exception>
    /// <exception cref="IOException">Thrown when <paramref name="zipFile"/> exists and <paramref name="overwrite"/> is false.</exception>
    public static PlutoniumPath PackZip(PlutoniumPath sourceDirectory, PlutoniumPath zipFile, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(zipFile);

        if (!System.IO.Directory.Exists(sourceDirectory.Value))
        {
            throw new DirectoryNotFoundException($"Source directory '{sourceDirectory.Value}' does not exist.");
        }

        if (File.Exists(zipFile.Value))
        {
            if (!overwrite)
            {
                throw new IOException($"Zip file '{zipFile.Value}' already exists.");
            }

            File.Delete(zipFile.Value);
        }

        var zipParent = Path.GetDirectoryName(Path.GetFullPath(zipFile.Value));
        if (!string.IsNullOrEmpty(zipParent))
        {
            System.IO.Directory.CreateDirectory(zipParent);
        }

        ZipFile.CreateFromDirectory(sourceDirectory.Value, zipFile.Value, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipFile;
    }

    /// <summary>
    /// Unpacks a zip archive into a directory, creating it as needed.
    /// </summary>
    /// <param name="zipFile">The zip file. Must not be null and must exist.</param>
    /// <param name="destinationDirectory">The destination directory. Must not be null.</param>
    /// <param name="overwriteFiles">Whether to overwrite existing files.</param>
    /// <returns>The destination directory. Never null.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="zipFile"/> does not exist.</exception>
    public static PlutoniumPath UnpackZip(PlutoniumPath zipFile, PlutoniumPath destinationDirectory, bool overwriteFiles = false)
    {
        ArgumentNullException.ThrowIfNull(zipFile);
        ArgumentNullException.ThrowIfNull(destinationDirectory);

        if (!File.Exists(zipFile.Value))
        {
            throw new FileNotFoundException($"Zip file '{zipFile.Value}' does not exist.", zipFile.Value);
        }

        System.IO.Directory.CreateDirectory(destinationDirectory.Value);
        ZipFile.ExtractToDirectory(zipFile.Value, destinationDirectory.Value, overwriteFiles);
        return destinationDirectory;
    }

    /// <summary>
    /// Reads and deserializes a JSON file.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="path">The JSON file. Must not be null.</param>
    /// <returns>The deserialized document. Never null.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="path"/> does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the file deserializes to null.</exception>
    public static T ReadJson<T>(PlutoniumPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path.Value))
        {
            throw new FileNotFoundException($"JSON file '{path.Value}' does not exist.", path.Value);
        }

        var json = File.ReadAllText(path.Value);
        var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException($"JSON file '{path.Value}' deserialized to null.");
        }

        return result;
    }

    /// <summary>
    /// Serializes a value as indented JSON and writes it to a file, creating parents as needed.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="path">The destination file. Must not be null.</param>
    /// <param name="value">The value to write. Must not be null.</param>
    /// <returns>The destination file path. Never null.</returns>
    public static PlutoniumPath WriteJson<T>(PlutoniumPath path, T value)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);

        var parent = Path.GetDirectoryName(Path.GetFullPath(path.Value));
        if (!string.IsNullOrEmpty(parent))
        {
            System.IO.Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path.Value, JsonSerializer.Serialize(value, JsonOptions));
        return path;
    }

    /// <summary>
    /// Computes the lowercase hex digest of a file with the given algorithm.
    /// </summary>
    /// <param name="path">The file. Must not be null and must exist.</param>
    /// <param name="algorithm">The hash algorithm.</param>
    /// <returns>The lowercase hex digest. Never null.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="path"/> does not exist.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="algorithm"/> is unknown.</exception>
    public static string ComputeFileHash(PlutoniumPath path, PlutoniumHashAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path.Value))
        {
            throw new FileNotFoundException($"File '{path.Value}' does not exist.", path.Value);
        }

        using HashAlgorithm hasher = algorithm switch
        {
            PlutoniumHashAlgorithm.Md5 => MD5.Create(),
            PlutoniumHashAlgorithm.Sha1 => SHA1.Create(),
            PlutoniumHashAlgorithm.Sha256 => SHA256.Create(),
            PlutoniumHashAlgorithm.Sha512 => SHA512.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown hash algorithm."),
        };
        using var stream = File.OpenRead(path.Value);
        return Convert.ToHexString(hasher.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the lowercase hex MD5 digest of a file.
    /// </summary>
    /// <param name="path">The file. Must not be null and must exist.</param>
    /// <returns>The lowercase hex digest. Never null.</returns>
    public static string ComputeMd5(PlutoniumPath path) => ComputeFileHash(path, PlutoniumHashAlgorithm.Md5);

    /// <summary>
    /// Computes the lowercase hex SHA-1 digest of a file.
    /// </summary>
    /// <param name="path">The file. Must not be null and must exist.</param>
    /// <returns>The lowercase hex digest. Never null.</returns>
    public static string ComputeSha1(PlutoniumPath path) => ComputeFileHash(path, PlutoniumHashAlgorithm.Sha1);

    /// <summary>
    /// Computes the lowercase hex SHA-256 digest of a file.
    /// </summary>
    /// <param name="path">The file. Must not be null and must exist.</param>
    /// <returns>The lowercase hex digest. Never null.</returns>
    public static string ComputeSha256(PlutoniumPath path) => ComputeFileHash(path, PlutoniumHashAlgorithm.Sha256);

    /// <summary>
    /// Computes the lowercase hex SHA-512 digest of a file.
    /// </summary>
    /// <param name="path">The file. Must not be null and must exist.</param>
    /// <returns>The lowercase hex digest. Never null.</returns>
    public static string ComputeSha512(PlutoniumPath path) => ComputeFileHash(path, PlutoniumHashAlgorithm.Sha512);
}

/// <summary>
/// Self-cleaning temp directory scope: creates a unique directory under the
/// system temp root and deletes it recursively on dispose.
/// </summary>
public sealed class PlutoniumTempDirectory : IDisposable
{
    private bool _disposed;

    /// <summary>Gets the temp directory path. Never null.</summary>
    public PlutoniumPath Directory { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumTempDirectory"/> class.
    /// Creates a unique directory under <see cref="Path.GetTempPath"/>.
    /// </summary>
    public PlutoniumTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "plutonium-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(path);
        Directory = new PlutoniumPath(path);
    }

    /// <summary>
    /// Deletes the temp directory recursively; a missing directory is tolerated.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (System.IO.Directory.Exists(Directory.Value))
            {
                System.IO.Directory.Delete(Directory.Value, recursive: true);
            }
        }
    }
}
