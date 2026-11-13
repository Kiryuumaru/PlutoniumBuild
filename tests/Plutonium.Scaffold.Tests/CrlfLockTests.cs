using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins line-ending normalization for the scaffold source and lock: a Windows
/// checkout can hand init and update CRLF files while the lock records LF
/// hashes, so both scripts must normalize before parsing or hashing.
/// </summary>
public sealed class CrlfLockTests
{
    private static readonly string[] PreservedPaths =
    [
        "build/build.cs",
        "build/build.slnx",
        "build/Directory.Build.props",
        "build/Directory.Build.targets",
    ];

    /// <summary>Init accepts a CRLF lock and payload and installs LF files.</summary>
    [Fact]
    public async Task Init_AcceptsCrlfLockAndPayload()
    {
        using var workspace = TempWorkspace.Create("init-crlf");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        DisableAutoCrlf(source.Repository);
        var sourceLockPath = Path.Combine(source.ScaffoldBuildPath, "plutonium.lock");
        RewriteTreeToCrlf(Path.Combine(source.SourcePath, "scaffold"));
        Assert.Contains("\r\n", File.ReadAllText(sourceLockPath), StringComparison.Ordinal);
        var crlfSha = source.Repository.Commit("crlf scaffold source");

        var init = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            ["--source", source.SourcePath, "--sha", crlfSha],
            target.RootPath);
        ScaffoldTestContext.AssertSucceeded(init, "init from a CRLF source");

        var installedLockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
        AssertLf(installedLockPath);
        foreach (var file in Directory.GetFiles(
                     Path.Combine(target.RootPath, "build"),
                     "*",
                     SearchOption.AllDirectories))
        {
            AssertLf(file);
        }

        foreach (var wrapper in new[] { "build.sh", "build.ps1", "build.cmd" })
        {
            AssertLf(Path.Combine(target.RootPath, wrapper));
        }

        var installedLock = PlutoniumLockFile.Load(installedLockPath);
        Assert.Equal(crlfSha, installedLock.GetValue("scaffold_commit"));
        Assert.Equal("0.1.0", installedLock.GetValue("package_version"));
        Assert.Equal(
            ScaffoldTestContext.NormalizeSourceValue(source.SourcePath),
            ScaffoldTestContext.NormalizeSourceValue(installedLock.GetValue("source") ?? string.Empty));
        AssertPreservedEntries(installedLock, target.RootPath);
        AssertManagedHashesMatch(installedLock, target.RootPath);
    }

    /// <summary>Update accepts a CRLF source and CRLF installed lock and installs LF files.</summary>
    [Fact]
    public async Task Update_AcceptsCrlfLockAndSource()
    {
        using var workspace = TempWorkspace.Create("update-crlf");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var init = await ScaffoldTestContext.RunInitAsync(source, target);
        ScaffoldTestContext.AssertSucceeded(init, "init");
        var initSha = source.HeadSha;

        DisableAutoCrlf(source.Repository);
        source.CommitPackageVersion("0.2.0");
        source.CommitChange("scaffold/build/build.sh", text => text + "\n# updated managed script\n");
        RewriteTreeToCrlf(Path.Combine(source.SourcePath, "scaffold"));
        var crlfSha = source.Repository.Commit("crlf scaffold update");

        var installedLockPath = Path.Combine(target.RootPath, "build", "plutonium.lock");
        RewriteToCrlf(installedLockPath);
        Assert.Contains("\r\n", File.ReadAllText(installedLockPath), StringComparison.Ordinal);

        var update = await ScaffoldTestContext.RunUpdateAsync(target, source, crlfSha);
        ScaffoldTestContext.AssertSucceeded(update, "update from a CRLF source and lock");

        AssertLf(installedLockPath);
        var installedLock = PlutoniumLockFile.Load(installedLockPath);
        Assert.Equal(crlfSha, installedLock.GetValue("scaffold_commit"));
        Assert.Equal("0.2.0", installedLock.GetValue("package_version"));
        Assert.Equal(
            ScaffoldTestContext.NormalizeSourceValue(source.SourcePath),
            ScaffoldTestContext.NormalizeSourceValue(installedLock.GetValue("source") ?? string.Empty));
        Assert.Equal(
            ScaffoldTestContext.NormalizeSourceValue(source.SourcePath),
            ScaffoldTestContext.NormalizeSourceValue(installedLock.GetValue("source_url") ?? string.Empty));
        AssertPreservedEntries(installedLock, target.RootPath);
        AssertManagedHashesMatch(installedLock, target.RootPath);

        var installedBuildScript = Path.Combine(target.RootPath, "build", "build.sh");
        AssertLf(installedBuildScript);
        Assert.Contains(
            "updated managed script",
            File.ReadAllText(installedBuildScript),
            StringComparison.Ordinal);
        var installedProject = Path.Combine(target.RootPath, "build", "build.csproj");
        AssertLf(installedProject);
        Assert.Equal("0.2.0", ScaffoldTestContext.PackageReferenceVersion(installedProject));
        AssertLf(Path.Combine(target.RootPath, "build", "global.json"));
        AssertLf(Path.Combine(target.RootPath, "build.sh"));

        var journalPath = Directory.GetFiles(
            Path.Combine(target.RootPath, "build", "temp", "update"),
            "journal.txt",
            SearchOption.AllDirectories).Single();
        var journal = File.ReadAllLines(journalPath);
        Assert.Contains("from_commit=" + initSha, journal);
        Assert.Contains("to_commit=" + crlfSha, journal);
        Assert.Contains("package_version=0.2.0", journal);

        var solutionPath = Path.Combine(target.RootPath, "build", "build.slnx");
        File.AppendAllText(solutionPath, "<!-- consumer drift -->\n");
        RewriteToCrlf(installedLockPath);
        var before = DirectorySnapshot.Capture(
            target.RootPath,
            ".git",
            ".test-home",
            "temp",
            "bin",
            "obj");
        var second = await ScaffoldTestContext.RunUpdateAsync(target, source, crlfSha);
        ScaffoldTestContext.AssertSucceeded(second, "second update from a CRLF lock");
        Assert.Contains("nothing to update", second.StandardOutput, StringComparison.OrdinalIgnoreCase);

        var differences = before.Differences(DirectorySnapshot.Capture(
            target.RootPath,
            ".git",
            ".test-home",
            "temp",
            "bin",
            "obj"));
        Assert.True(
            differences.Count == 0,
            "Up-to-date update changed the tree: " + string.Join(", ", differences));
    }

    /// <summary>The scaffold payload is checked out with LF regardless of autocrlf.</summary>
    [Fact]
    public async Task ScaffoldPayload_ForcesLfCheckout()
    {
        var layout = RepositoryLayout.Locate();
        var tracked = await GitNullSeparatedAsync(
            layout.Root,
            "ls-files",
            "-z",
            "--",
            "scaffold/build",
            "scaffold/root");
        Assert.NotEmpty(tracked);
        Assert.Contains("scaffold/build/plutonium.lock", tracked);

        var eol = await GitAttributesAsync(layout.Root, "eol", tracked);
        Assert.Equal(tracked.Count, eol.Count);
        foreach (var pair in eol)
        {
            Assert.Equal("lf", pair.Value);
        }

        var text = await GitAttributesAsync(layout.Root, "text", tracked);
        Assert.Equal(tracked.Count, text.Count);
        foreach (var pair in text)
        {
            Assert.Equal("set", pair.Value);
        }

        var lockFile = PlutoniumLockFile.Load(
            Path.Combine(layout.ScaffoldBuildDirectory, "plutonium.lock"));
        foreach (var relative in TaggedPaths(lockFile, "managed"))
        {
            var path = PayloadPath(layout.Root, relative);
            Assert.True(File.Exists(path), "Missing scaffold payload file: " + relative);
            Assert.Equal(lockFile.GetHash(relative), Hashing.Sha256File(path));
        }
    }

    private static void DisableAutoCrlf(GitRepository repository)
    {
        AssertGitSucceeded(repository.Run("config", "core.autocrlf", "false"), "git config core.autocrlf");
        AssertGitSucceeded(repository.Run("config", "core.eol", "lf"), "git config core.eol");
    }

    private static void AssertGitSucceeded(ProcessResult result, string operation)
    {
        Assert.True(result.Succeeded, operation + " failed." + Environment.NewLine + result.Describe());
    }

    private static void RewriteTreeToCrlf(string root)
    {
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            RewriteToCrlf(file);
        }
    }

    private static void RewriteToCrlf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var lf = new List<byte>(bytes.Length);
        foreach (var value in bytes)
        {
            if (value != (byte)'\r')
            {
                lf.Add(value);
            }
        }

        var crlf = new List<byte>(lf.Count + (lf.Count / 8));
        foreach (var value in lf)
        {
            if (value == (byte)'\n')
            {
                crlf.Add((byte)'\r');
            }

            crlf.Add(value);
        }

        File.WriteAllBytes(path, crlf.ToArray());
    }

    private static void AssertLf(string path)
    {
        Assert.True(File.Exists(path), "Expected file not found: " + path);
        Assert.DoesNotContain("\r", File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static void AssertPreservedEntries(PlutoniumLockFile lockFile, string targetRoot)
    {
        var preserved = TaggedPaths(lockFile, "preserve").ToArray();
        var managed = TaggedPaths(lockFile, "managed").ToArray();
        foreach (var relative in PreservedPaths)
        {
            Assert.Contains(relative, preserved);
            Assert.True(
                File.Exists(InstalledPath(targetRoot, relative)),
                "Missing preserved file: " + relative);
        }

        foreach (var relative in preserved.Except(managed, StringComparer.Ordinal))
        {
            Assert.Null(lockFile.GetHash(relative));
        }
    }

    private static void AssertManagedHashesMatch(PlutoniumLockFile lockFile, string targetRoot)
    {
        var managed = TaggedPaths(lockFile, "managed").ToArray();
        Assert.NotEmpty(managed);
        foreach (var relative in managed)
        {
            var path = InstalledPath(targetRoot, relative);
            Assert.True(File.Exists(path), "Missing managed file: " + relative);
            Assert.Equal(lockFile.GetHash(relative), Hashing.Sha256File(path));
        }
    }

    private static IEnumerable<string> TaggedPaths(PlutoniumLockFile lockFile, string tag)
    {
        foreach (var line in lockFile.Lines)
        {
            var fields = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && string.Equals(fields[0], tag, StringComparison.Ordinal))
            {
                yield return fields[1];
            }
        }
    }

    private static string InstalledPath(string targetRoot, string relative) =>
        relative.StartsWith("build/", StringComparison.Ordinal)
            ? Path.Combine(
                targetRoot,
                "build",
                relative["build/".Length..].Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string PayloadPath(string repositoryRoot, string relative) =>
        relative.StartsWith("build/", StringComparison.Ordinal)
            ? Path.Combine(
                repositoryRoot,
                "scaffold",
                "build",
                relative["build/".Length..].Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(
                repositoryRoot,
                "scaffold",
                "root",
                relative.Replace('/', Path.DirectorySeparatorChar));

    private static async Task<IReadOnlyList<string>> GitNullSeparatedAsync(
        string root,
        params string[] arguments)
    {
        var result = await RunGitAsync(root, arguments);
        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, string>> GitAttributesAsync(
        string root,
        string attribute,
        IReadOnlyList<string> paths)
    {
        var arguments = new List<string> { "check-attr", "-z", attribute, "--" };
        arguments.AddRange(paths);
        var result = await RunGitAsync(root, arguments.ToArray());
        var fields = result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, fields.Length % 3);
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < fields.Length; index += 3)
        {
            Assert.Equal(attribute, fields[index + 1]);
            attributes[fields[index]] = fields[index + 2];
        }

        Assert.Equal(paths.Count, attributes.Count);
        return attributes;
    }

    private static async Task<ProcessResult> RunGitAsync(string root, params string[] arguments)
    {
        var allArguments = new List<string> { "-C", root };
        allArguments.AddRange(arguments);
        var result = await ProcessHarness.RunAsync(
            new ProcessRequest(
                "git",
                allArguments,
                root,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["GIT_CONFIG_NOSYSTEM"] = "1",
                    ["GIT_TERMINAL_PROMPT"] = "0",
                }));
        AssertGitSucceeded(result, "git " + string.Join(' ', arguments));
        return result;
    }
}
