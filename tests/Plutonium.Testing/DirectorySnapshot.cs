using System.Security.Cryptography;

namespace Plutonium.Testing;

/// <summary>
/// Captures file paths, content hashes, and Unix execute bits for a directory
/// tree so tests can prove that an operation left a tree untouched.
/// </summary>
public sealed class DirectorySnapshot
{
    private readonly SortedDictionary<string, string> _entries;

    private DirectorySnapshot(SortedDictionary<string, string> entries)
    {
        _entries = entries;
    }

    /// <summary>Gets the captured entries keyed by repository-relative path.</summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    /// <summary>Captures a directory tree.</summary>
    /// <param name="root">The directory to capture.</param>
    /// <param name="excludedNames">Directory or file names to skip at any depth.</param>
    /// <returns>The captured snapshot.</returns>
    public static DirectorySnapshot Capture(string root, params string[] excludedNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            CaptureDirectory(root, root, entries, new HashSet<string>(excludedNames, StringComparer.OrdinalIgnoreCase));
        }

        return new DirectorySnapshot(entries);
    }

    /// <summary>Lists the paths that differ between two snapshots.</summary>
    /// <param name="other">The other snapshot.</param>
    /// <returns>Human-readable differences, or an empty list when the trees match.</returns>
    public IReadOnlyList<string> Differences(DirectorySnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var differences = new List<string>();
        foreach (var pair in _entries)
        {
            if (!other._entries.TryGetValue(pair.Key, out var otherValue))
            {
                differences.Add("missing in other: " + pair.Key);
            }
            else if (!string.Equals(pair.Value, otherValue, StringComparison.Ordinal))
            {
                differences.Add("changed: " + pair.Key);
            }
        }

        foreach (var pair in other._entries)
        {
            if (!_entries.ContainsKey(pair.Key))
            {
                differences.Add("added: " + pair.Key);
            }
        }

        return differences;
    }

    private static void CaptureDirectory(
        string root,
        string current,
        IDictionary<string, string> entries,
        ISet<string> excludedNames)
    {
        foreach (var directory in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (excludedNames.Contains(name))
            {
                continue;
            }

            entries[RelativePath(root, directory)] = "<directory>";
            CaptureDirectory(root, directory, entries, excludedNames);
        }

        foreach (var file in Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (excludedNames.Contains(name))
            {
                continue;
            }

            var relativePath = RelativePath(root, file);
            entries[relativePath] = HashFile(file) + ModeSuffix(file);
        }
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ModeSuffix(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            var executable = mode &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            return executable == 0 ? string.Empty : ":exec";
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
