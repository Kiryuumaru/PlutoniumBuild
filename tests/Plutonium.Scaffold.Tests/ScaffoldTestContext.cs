using System.Xml.Linq;
using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>Shared setup for consumer repositories and scaffold script runs.</summary>
internal static class ScaffoldTestContext
{
    private static readonly Lazy<ScaffoldManifest> Manifest = new(LoadManifest);

    internal static IReadOnlyList<string> ManagedFiles => Manifest.Value.ManagedFiles;

    internal static IReadOnlyList<string> RequiredFiles => Manifest.Value.RequiredFiles;

    private const string SolutionText = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.0.31903.59
        MinimumVisualStudioVersion = 10.0.40219.1
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Global
        EndGlobal
        """;

    internal static GitRepository CreateConsumerRepository(
        TempWorkspace workspace,
        bool withSolution = true,
        string name = "consumer")
    {
        var repository = GitRepository.Create(workspace, name);
        repository.WriteFile("sentinel.txt", "do not touch\n");
        repository.WriteFile("README.md", "# Consumer\n");
        if (withSolution)
        {
            repository.WriteFile("Consumer.sln", SolutionText + "\n");
            repository.WriteFile(
                "src/App/App.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
        }

        repository.Commit("consumer baseline");
        return repository;
    }

    internal static async Task<ProcessResult> RunInitAsync(
        ScaffoldSource source,
        GitRepository target,
        string? refName = null,
        bool useDefaultRef = false,
        string? sha = null,
        params string[] extraArguments)
    {
        Assert.True(File.Exists(source.InitScriptPath), "Init script not found: " + source.InitScriptPath);
        var arguments = new List<string>
        {
            "--source",
            source.SourcePath,
        };
        if (refName is not null)
        {
            arguments.Add("--ref");
            arguments.Add(refName);
        }
        else if (!useDefaultRef)
        {
            arguments.Add("--sha");
            arguments.Add(sha ?? source.HeadSha);
        }

        arguments.AddRange(extraArguments);
        return await RunScriptAsync(source.InitScriptPath, arguments, target.RootPath);
    }

    internal static async Task<ProcessResult> RunUpdateAsync(
        GitRepository target,
        ScaffoldSource source,
        string? sha = null,
        bool includeSource = true,
        string? refName = null,
        params string[] extraArguments)
    {
        var updateScript = Path.Combine(target.RootPath, "build", "update.sh");
        Assert.True(File.Exists(updateScript), "Update script not found: " + updateScript);
        var arguments = new List<string>();
        if (includeSource)
        {
            arguments.Add("--source");
            arguments.Add(source.SourcePath);
        }

        if (refName is not null)
        {
            arguments.Add("--ref");
            arguments.Add(refName);
        }
        else if (sha is not null)
        {
            arguments.Add("--sha");
            arguments.Add(sha);
        }

        arguments.AddRange(extraArguments);
        return await RunScriptAsync(updateScript, arguments, target.RootPath);
    }

    internal static Task<ProcessResult> RunScriptAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var environment = CreateScriptEnvironment(workingDirectory);
        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        return ProcessHarness.RunBashAsync(
            scriptPath,
            arguments,
            workingDirectory,
            environment,
            TimeSpan.FromMinutes(10));
    }

    internal static PlutoniumLockFile ReadLock(GitRepository target)
    {
        var path = Path.Combine(target.RootPath, "build", "plutonium.lock");
        Assert.True(File.Exists(path), "Installed lock not found: " + path);
        return PlutoniumLockFile.Load(path);
    }

    internal static string ReadScaffoldCommit(GitRepository target)
    {
        var value = ReadLock(target).GetValue("scaffold_commit");
        Assert.False(string.IsNullOrWhiteSpace(value), "The installed lock has no scaffold_commit.");
        Assert.Matches("^[0-9a-f]{40}$", value!);
        return value!;
    }

    internal static void AssertScaffoldCommit(GitRepository target, string expected)
    {
        var actual = ReadScaffoldCommit(target);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("refs/", actual, StringComparison.Ordinal);
    }

    internal static IReadOnlyDictionary<string, string> ReadUpdateJournal(GitRepository target)
    {
        var updateRoot = Path.Combine(target.RootPath, "build", "temp", "update");
        Assert.True(Directory.Exists(updateRoot), "Update journal directory not found: " + updateRoot);
        var journalPath = Directory.GetFiles(updateRoot, "journal.txt", SearchOption.AllDirectories).Single();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(journalPath))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                values[line[..separator]] = line[(separator + 1)..];
            }
        }

        return values;
    }

    internal static async Task<string> ResolveGitPathAsync(TempWorkspace workspace)
    {
        var probePath = workspace.PathFor("resolve-git.sh");
        await File.WriteAllTextAsync(
            probePath,
            """
            #!/usr/bin/env bash
            command -v git
            """);
        var probe = await ProcessHarness.RunBashAsync(probePath, [], workspace.Root);
        Assert.True(probe.Succeeded, "Could not resolve git." + Environment.NewLine + probe.Describe());
        var path = probe.StandardOutput.Trim();
        Assert.False(string.IsNullOrWhiteSpace(path), "git was not found on PATH.");
        return path;
    }

    internal static FakeToolchain CreateLoggingGitShim(
        TempWorkspace workspace,
        string realGitPath,
        string logPath)
    {
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddDotNet("10.0.402", 0);
        toolchain.AddShim(
            "git",
            """
            #!/usr/bin/env bash
            printf '%s\n' "$*" >> "$GIT_LOG"
            exec "$REAL_GIT" "$@"
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["REAL_GIT"] = realGitPath,
                ["GIT_LOG"] = ToBashPath(logPath),
            });
        return toolchain;
    }

    internal static FakeToolchain CreateGitInvocationProbe(
        TempWorkspace workspace,
        string markerPath)
    {
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddShim(
            "git",
            """
            #!/usr/bin/env bash
            printf 'invoked\n' > "$GIT_MARKER"
            exit 1
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GIT_MARKER"] = ToBashPath(markerPath),
            });
        return toolchain;
    }

    internal static string ToBashPath(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    /// <summary>
    /// Builds the environment for scaffold scripts. <c>TMPDIR</c> is scoped to
    /// the test workspace so <c>mktemp</c> never uses the shared MSYS
    /// <c>/tmp</c> mount on Windows. <c>TMP</c> and <c>TEMP</c> are not
    /// overridden: the child inherits the parent's values, and Git for Windows
    /// derives that shared mount from them.
    /// </summary>
    /// <param name="workingDirectory">The directory the script runs in.</param>
    /// <returns>The script environment.</returns>
    internal static Dictionary<string, string?> CreateScriptEnvironment(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var workspaceDirectory = Path.GetDirectoryName(Path.GetFullPath(workingDirectory))
            ?? throw new InvalidOperationException(
                "The script working directory has no parent: " + workingDirectory);
        var baseDirectory = Path.GetFullPath(TempWorkspace.BaseDirectory);
        var relativeWorkspace = Path.GetRelativePath(baseDirectory, workspaceDirectory);
        // A caller that passes the workspace root as the working directory
        // would otherwise create the temp directory in the shared base and
        // reintroduce the cross-assembly race.
        if (Path.IsPathRooted(relativeWorkspace) ||
            relativeWorkspace == "." ||
            relativeWorkspace == ".." ||
            relativeWorkspace.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The script working directory must be below a test workspace, not the shared test root: " +
                workingDirectory);
        }

        var home = Path.Combine(workspaceDirectory, ".test-home");
        var temporary = Path.Combine(workspaceDirectory, ".test-tmp");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(temporary);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = home,
            ["TMPDIR"] = temporary,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
        };
    }

    internal static void AssertSucceeded(ProcessResult result, string operation)
    {
        Assert.True(result.Succeeded, operation + " failed." + Environment.NewLine + result.Describe());
    }

    internal static void AssertFailed(ProcessResult result, string operation)
    {
        Assert.False(result.Succeeded, operation + " unexpectedly succeeded." + Environment.NewLine + result.Describe());
    }

    /// <summary>
    /// Normalizes a source value for comparison. The harness hands Git Bash a
    /// forward-slash path on Windows, so an installed lock can record a
    /// different separator than <see cref="ScaffoldSource.SourcePath"/>.
    /// </summary>
    /// <param name="value">The recorded or expected source value.</param>
    /// <returns>The value with forward slashes and no trailing separator.</returns>
    internal static string NormalizeSourceValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace('\\', '/').TrimEnd('/');
    }

    internal enum LockPoison
    {
        PrefixedSource,
        PrefixedScaffoldCommit,
        NonCanonicalSource,
        DuplicateFormat,
        ShortScaffoldCommit,
        MissingFormat,
    }

    internal static string ApplyLockPoison(string content, LockPoison poison)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var trailingNewline = normalized.EndsWith('\n');
        var lines = normalized.Split('\n').ToList();
        if (trailingNewline)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        switch (poison)
        {
            case LockPoison.PrefixedSource:
                PrefixLockLine(lines, "source=");
                break;
            case LockPoison.PrefixedScaffoldCommit:
                PrefixLockLine(lines, "scaffold_commit=");
                break;
            case LockPoison.NonCanonicalSource:
                var sourceIndex = lines.FindIndex(
                    line => line.StartsWith("source=", StringComparison.Ordinal));
                Assert.True(sourceIndex >= 0, "The lock has no line starting with 'source='.");
                lines[sourceIndex] = "source = " + lines[sourceIndex]["source=".Length..];
                break;
            case LockPoison.DuplicateFormat:
                lines.Add("format=1");
                break;
            case LockPoison.ShortScaffoldCommit:
                ReplaceLockLine(lines, "scaffold_commit=", "scaffold_commit=abc1234");
                break;
            case LockPoison.MissingFormat:
                Assert.True(
                    lines.RemoveAll(line => line.StartsWith("format=", StringComparison.Ordinal)) == 1,
                    "The lock must carry exactly one canonical format key.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(poison), poison, "Unknown lock poison.");
        }

        return string.Join("\n", lines) + (trailingNewline ? "\n" : string.Empty);
    }

    internal static string LockPoisonMessage(LockPoison poison) => poison switch
    {
        LockPoison.PrefixedSource => "reserved key source must start at column 0",
        LockPoison.PrefixedScaffoldCommit => "reserved key scaffold_commit must start at column 0",
        LockPoison.NonCanonicalSource => "reserved key source has a non-canonical form",
        LockPoison.DuplicateFormat => "duplicate reserved key format",
        LockPoison.ShortScaffoldCommit => "scaffold_commit must be a full 40-character commit SHA",
        LockPoison.MissingFormat => "missing required key format",
        _ => throw new ArgumentOutOfRangeException(nameof(poison), poison, "Unknown lock poison."),
    };

    internal static void WriteLockPoison(string path, LockPoison poison)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ApplyLockPoison(File.ReadAllText(path), poison));
    }

    private static void PrefixLockLine(List<string> lines, string prefix)
    {
        var index = lines.FindIndex(line => line.StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(index >= 0, "The lock has no line starting with '" + prefix + "'.");
        lines[index] = " " + lines[index];
    }

    private static void ReplaceLockLine(List<string> lines, string prefix, string replacement)
    {
        var index = lines.FindIndex(line => line.StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(index >= 0, "The lock has no line starting with '" + prefix + "'.");
        lines[index] = replacement;
    }

    private static ScaffoldManifest LoadManifest()
    {
        var layout = RepositoryLayout.Locate();
        var lockPath = Path.Combine(layout.ScaffoldBuildDirectory, "plutonium.lock");
        Assert.True(File.Exists(lockPath), "Scaffold lock not found: " + lockPath);

        var managed = new List<string>();
        var preserved = new List<string>();
        foreach (var line in File.ReadLines(lockPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            if (fields[0] == "managed")
            {
                Assert.True(fields.Length == 3, "Malformed managed lock line: " + trimmed);
                Assert.True(
                    fields[2].Length == 64 && fields[2].All(char.IsAsciiHexDigit),
                    "Managed lock line does not carry a SHA-256 hash: " + trimmed);
                managed.Add(fields[1]);
            }
            else if (fields[0] == "preserve")
            {
                Assert.True(fields.Length == 2, "Malformed preserve lock line: " + trimmed);
                preserved.Add(fields[1]);
            }
        }

        Assert.NotEmpty(managed);
        Assert.NotEmpty(preserved);
        var required = managed
            .Concat(preserved)
            .Append("build/plutonium.lock")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return new ScaffoldManifest(
            managed.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            required);
    }

    internal static XDocument ReadXml(string path)
    {
        Assert.True(File.Exists(path), "Expected file not found: " + path);
        return XDocument.Load(path);
    }

    internal static string ReadText(string path)
    {
        Assert.True(File.Exists(path), "Expected file not found: " + path);
        return File.ReadAllText(path);
    }

    internal static string PackageReferenceVersion(string projectPath)
    {
        var document = ReadXml(projectPath);
        var reference = document.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "PackageReference" &&
                string.Equals(
                    element.Attribute("Include")?.Value,
                    "PlutoniumBuild",
                    StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(reference);
        return reference.Attribute("Version")?.Value
               ?? reference.Elements().FirstOrDefault(element => element.Name.LocalName == "Version")?.Value
               ?? string.Empty;
    }

    private sealed class ScaffoldManifest
    {
        public ScaffoldManifest(IReadOnlyList<string> managedFiles, IReadOnlyList<string> requiredFiles)
        {
            ManagedFiles = managedFiles;
            RequiredFiles = requiredFiles;
        }

        public IReadOnlyList<string> ManagedFiles { get; }

        public IReadOnlyList<string> RequiredFiles { get; }
    }
}
