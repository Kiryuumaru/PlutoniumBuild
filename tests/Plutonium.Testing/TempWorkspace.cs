namespace Plutonium.Testing;

/// <summary>
/// Owns a unique temporary directory. Workspaces are removed on disposal;
/// <c>PLUTONIUM_KEEP_TEMP=1</c> preserves them for diagnosis.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private TempWorkspace(string root)
    {
        Root = root;
    }

    /// <summary>Gets the absolute path of the workspace root.</summary>
    public string Root { get; }

    /// <summary>Creates a new, uniquely named workspace below the test temp root.</summary>
    /// <param name="name">A short, human-readable prefix for the workspace name.</param>
    /// <returns>The created workspace.</returns>
    public static TempWorkspace Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var baseDirectory = Environment.GetEnvironmentVariable("PLUTONIUM_TEST_TEMP");
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = OperatingSystem.IsWindows()
                ? Path.Combine(Path.GetTempPath(), "pb")
                : Path.Combine("/tmp/opencode", "pb");
        }

        var safeName = new string(name
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());
        var path = Path.Combine(baseDirectory, safeName + "-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(path);
        return new TempWorkspace(path);
    }

    /// <summary>Combines the workspace root with a relative path.</summary>
    /// <param name="relativePath">One or more path segments.</param>
    /// <returns>The absolute path.</returns>
    public string PathFor(params string[] relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var segments = new string[relativePath.Length + 1];
        segments[0] = Root;
        Array.Copy(relativePath, 0, segments, 1, relativePath.Length);
        return Path.Combine(segments);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var keep = string.Equals(
            Environment.GetEnvironmentVariable("PLUTONIUM_KEEP_TEMP"),
            "1",
            StringComparison.Ordinal);

        if (keep)
        {
            Console.WriteLine("Preserved test workspace: " + Root);
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    ClearReadOnlyAttributes(Root);
                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            ClearReadOnlyAttributes(entry);
        }
    }
}
