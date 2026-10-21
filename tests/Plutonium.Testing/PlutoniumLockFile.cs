using System.Text.RegularExpressions;

namespace Plutonium.Testing;

/// <summary>
/// Reads the line-oriented <c>plutonium.lock</c> scaffold manifest without
/// assuming a single key/value separator or field order.
/// </summary>
public sealed partial class PlutoniumLockFile
{
    private readonly List<string> _lines;

    private PlutoniumLockFile(string path, List<string> lines)
    {
        Path = path;
        _lines = lines;
    }

    /// <summary>Gets the lock file path.</summary>
    public string Path { get; }

    /// <summary>Gets the file lines.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Loads a lock file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The parsed lock file.</returns>
    public static PlutoniumLockFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Lock file not found.", path);
        }

        return new PlutoniumLockFile(path, [.. File.ReadAllLines(path)]);
    }

    /// <summary>Attempts to read a scalar value by key.</summary>
    /// <param name="key">The key name, matched case-insensitively.</param>
    /// <returns>The value, or null when the key is absent.</returns>
    public string? GetValue(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach (var line in _lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var match = ScalarLine().Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["value"].Value.Trim();
            }
        }

        return null;
    }

    /// <summary>Finds a value that follows a textual marker such as <c>installer</c>.</summary>
    /// <param name="marker">A case-insensitive marker contained in the line.</param>
    /// <returns>The last whitespace-separated token, or null when no line matches.</returns>
    public string? FindValue(string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        foreach (var line in _lines)
        {
            if (!IsComment(line) &&
                line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                var tokens = line.Split(
                    [' ', '\t', '=', ':'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return tokens.Length == 0 ? null : tokens[^1];
            }
        }

        return null;
    }

    /// <summary>Counts lines that contain a marker and a 64-character SHA-256 hash.</summary>
    /// <param name="marker">A case-insensitive marker contained in the line.</param>
    /// <returns>The number of matching lines.</returns>
    public int CountHashedLines(string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        return _lines.Count(line =>
            !IsComment(line) &&
            line.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
            HashToken().IsMatch(line));
    }

    /// <summary>Finds the pinned hash for a managed file path.</summary>
    /// <param name="relativePath">The path as written in the lock file, for example <c>build/build.sh</c>.</param>
    /// <returns>The lowercase hash, or null when no line pins the path.</returns>
    public string? GetHash(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        foreach (var line in _lines)
        {
            if (!ContainsPathToken(line, relativePath))
            {
                continue;
            }

            var match = HashToken().Match(line);
            if (match.Success)
            {
                return match.Value.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>Finds every distinct 64-character hash in the file.</summary>
    /// <returns>The hashes in file order.</returns>
    public IReadOnlyList<string> GetHashes() =>
        _lines.SelectMany(line => HashToken().Matches(line).Select(match => match.Value.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Replaces the hash pinned for a managed file path.</summary>
    /// <param name="relativePath">The path as written in the lock file.</param>
    /// <param name="hash">The replacement SHA-256 hash.</param>
    /// <returns>True when a line was updated.</returns>
    public bool ReplaceHash(string relativePath, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        for (var index = 0; index < _lines.Count; index++)
        {
            if (!ContainsPathToken(_lines[index], relativePath))
            {
                continue;
            }

            var match = HashToken().Match(_lines[index]);
            if (!match.Success)
            {
                continue;
            }

            _lines[index] = _lines[index][..match.Index] + hash + _lines[index][(match.Index + match.Length)..];
            return true;
        }

        return false;
    }

    /// <summary>Replaces every SHA-256 hash on lines that contain a marker.</summary>
    /// <param name="marker">A case-insensitive marker such as <c>installer</c>.</param>
    /// <param name="hash">The replacement hash.</param>
    /// <returns>The number of lines updated.</returns>
    public int ReplaceHashesForMarker(string marker, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        var updated = 0;
        for (var index = 0; index < _lines.Count; index++)
        {
            if (IsComment(_lines[index]) ||
                !_lines[index].Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                !HashToken().IsMatch(_lines[index]))
            {
                continue;
            }

            _lines[index] = HashToken().Replace(_lines[index], hash);
            updated++;
        }

        return updated;
    }

    /// <summary>Sets a scalar value, updating the first matching line or appending a new one.</summary>
    /// <param name="key">The key name.</param>
    /// <param name="value">The replacement value.</param>
    public void SetValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        for (var index = 0; index < _lines.Count; index++)
        {
            var match = ScalarLine().Match(_lines[index].Trim());
            if (!match.Success ||
                !string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _lines[index] = key + "=" + value;
            return;
        }

        _lines.Add(key + "=" + value);
    }

    /// <summary>Writes the current lines back to disk.</summary>
    public void Save() => File.WriteAllLines(Path, _lines);

    private static bool ContainsPathToken(string line, string relativePath)
    {
        if (IsComment(line))
        {
            return false;
        }

        var target = NormalizePath(relativePath);
        var tokens = line.Replace('\\', '/').Split(
                [' ', '\t', '=', ':', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePath);
        return tokens.Contains(target, StringComparer.Ordinal);
    }

    private static bool IsComment(string line) =>
        line.TrimStart().StartsWith('#');

    private static string NormalizePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    [GeneratedRegex(
        @"^(?<key>[A-Za-z0-9_.\-]+)\s*(?:=|:|\s+)\s*(?<value>.+?)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ScalarLine();

    [GeneratedRegex("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])", RegexOptions.CultureInvariant)]
    private static partial Regex HashToken();
}
