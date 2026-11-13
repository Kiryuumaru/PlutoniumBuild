using Plutonium.Testing;
using Xunit;

namespace Plutonium.Scaffold.Tests;

/// <summary>
/// Pins the scaffold test harness to workspace-scoped temporary directories so
/// Git for Windows never routes scaffold work through the shared MSYS /tmp mount.
/// </summary>
public sealed class ScaffoldEnvironmentTests
{
    /// <summary>The script environment scopes TMPDIR to the workspace and does not override TMP/TEMP.</summary>
    [Fact]
    public void ScriptEnvironment_ScopesTmpDirToWorkspace()
    {
        using var workspace = TempWorkspace.Create("scaffold-env");
        var workingDirectory = workspace.PathFor("repos", "consumer");
        Directory.CreateDirectory(workingDirectory);

        var environment = ScaffoldTestContext.CreateScriptEnvironment(workingDirectory);

        Assert.True(environment.TryGetValue("TMPDIR", out var tmpDir), "TMPDIR is missing.");
        Assert.False(string.IsNullOrWhiteSpace(tmpDir), "TMPDIR is empty.");
        var fullTmpDir = Path.GetFullPath(tmpDir!);
        var relative = Path.GetRelativePath(workspace.Root, fullTmpDir);
        Assert.False(Path.IsPathRooted(relative), "TMPDIR escapes the workspace: " + fullTmpDir);
        Assert.False(
            relative.StartsWith("..", StringComparison.Ordinal),
            "TMPDIR escapes the workspace: " + fullTmpDir);
        Assert.True(Directory.Exists(fullTmpDir), "TMPDIR was not created: " + fullTmpDir);
        Assert.False(environment.ContainsKey("TMP"), "TMP must not be overridden.");
        Assert.False(environment.ContainsKey("TEMP"), "TEMP must not be overridden.");
    }

    /// <summary>A caller cannot place the script temp directory in the shared test root.</summary>
    [Fact]
    public void ScriptEnvironment_RejectsTheSharedTestRoot()
    {
        using var workspace = TempWorkspace.Create("scaffold-env-root");
        var exception = Assert.Throws<InvalidOperationException>(
            () => ScaffoldTestContext.CreateScriptEnvironment(workspace.Root));
        Assert.Contains("below a test workspace", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Smoke scripts must not override the shared temp variables for child processes.</summary>
    [Fact]
    public void SmokeScripts_DoNotOverrideSharedTempVariables()
    {
        var layout = RepositoryLayout.Locate();
        var scripts = Directory.GetFiles(
            Path.Combine(layout.Root, "scaffold", "smoke"),
            "*.sh",
            SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(scripts);

        var failures = new List<string>();
        foreach (var script in scripts)
        {
            var lines = File.ReadAllLines(script);
            for (var index = 0; index < lines.Length; index++)
            {
                var assignment = lines[index].TrimStart();
                if (assignment.StartsWith('#'))
                {
                    continue;
                }

                if (assignment.StartsWith("export ", StringComparison.Ordinal))
                {
                    assignment = assignment["export ".Length..].TrimStart();
                }

                if (assignment.StartsWith("TMP=", StringComparison.Ordinal) ||
                    assignment.StartsWith("TEMP=", StringComparison.Ordinal) ||
                    assignment.StartsWith("TMPDIR=", StringComparison.Ordinal))
                {
                    failures.Add(
                        RelativePath(layout.Root, script) + ":" + (index + 1) +
                        ": smoke script overrides a shared temp variable: " + lines[index].Trim());
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Smoke scripts must not override TMP, TEMP, or TMPDIR for child processes:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    /// <summary>Scaffold scripts keep mktemp and temporary paths inside TMPDIR or the repository.</summary>
    [Fact]
    public void ScaffoldScripts_RootMktempAndTemporaryPathsUnderTmpDir()
    {
        var layout = RepositoryLayout.Locate();
        var scripts = EnumerateScaffoldScripts(layout.Root).ToArray();
        Assert.NotEmpty(scripts);

        var failures = new List<string>();
        foreach (var script in scripts)
        {
            var lines = File.ReadAllLines(script);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith('#'))
                {
                    continue;
                }

                var usesTmpDir = line.Contains("TMPDIR", StringComparison.Ordinal);
                var usesSharedTempVariable =
                    line.Contains("$TMP", StringComparison.Ordinal) ||
                    line.Contains("$TEMP", StringComparison.Ordinal);
                var usesQuotedVariable =
                    line.Contains("\"$(", StringComparison.Ordinal) ||
                    line.Contains("\"$", StringComparison.Ordinal);
                if (line.Contains("mktemp", StringComparison.Ordinal) &&
                    !usesTmpDir &&
                    (usesSharedTempVariable || !usesQuotedVariable))
                {
                    failures.Add(
                        RelativePath(layout.Root, script) + ":" + (index + 1) +
                        ": mktemp template is not rooted in TMPDIR or a repository path: " + trimmed);
                }

                var withoutAllowedFallback = line.Replace(
                    "${TMPDIR:-/tmp}",
                    string.Empty,
                    StringComparison.Ordinal);
                if (withoutAllowedFallback.Contains("/tmp", StringComparison.Ordinal))
                {
                    failures.Add(
                        RelativePath(layout.Root, script) + ":" + (index + 1) +
                        ": bare /tmp path: " + trimmed);
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Scaffold scripts contain workspace-escaping temporary paths:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    /// <summary>Init creates its fetch directory under TMPDIR and removes it before returning.</summary>
    [Fact]
    public async Task Init_CreatesAndCleansFetchDirectoryUnderTmpDir()
    {
        using var workspace = TempWorkspace.Create("scaffold-tmpdir");
        var layout = RepositoryLayout.Locate();
        var source = await ScaffoldSource.CreateAsync(workspace, layout);
        var target = ScaffoldTestContext.CreateConsumerRepository(workspace);

        var sentinel = workspace.PathFor("tmp-sentinel");
        Directory.CreateDirectory(sentinel);
        var observedTmpDir = workspace.PathFor("observed-tmpdir.txt");
        var observedFetch = workspace.PathFor("observed-fetch.txt");

        var realGit = await ResolveGitPathAsync(workspace);
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddShim(
            "git",
            """
            #!/usr/bin/env bash
            printf '%s\n' "${TMPDIR:-}" > "$OBSERVED_TMPDIR"
            if [ -n "${TMPDIR:-}" ]; then
              find "$TMPDIR" -maxdepth 1 -mindepth 1 -type d -name 'plutonium-init.*' -print > "$OBSERVED_FETCH" 2>/dev/null || true
            fi
            exec "$REAL_GIT" "$@"
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["REAL_GIT"] = realGit,
                ["OBSERVED_TMPDIR"] = ToBashPath(observedTmpDir),
                ["OBSERVED_FETCH"] = ToBashPath(observedFetch),
            });

        var init = await ScaffoldTestContext.RunScriptAsync(
            source.InitScriptPath,
            ["--source", source.SourcePath, "--sha", source.HeadSha],
            target.RootPath,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = toolchain.BuildPath(),
                ["TMPDIR"] = sentinel,
            });
        ScaffoldTestContext.AssertSucceeded(init, "init");

        var observedTmp = await ReadSingleLineAsync(observedTmpDir, "TMPDIR observation");
        var fetch = await ReadSingleLineAsync(observedFetch, "fetch directory observation");
        Assert.True(
            IsUnderBashDirectory(fetch, observedTmp),
            "The fetch directory escaped TMPDIR: fetch='" + fetch + "', TMPDIR='" + observedTmp + "'.");

        await AssertBashPathExistsAsync(workspace, observedTmp, "TMPDIR");
        await AssertBashPathMissingAsync(workspace, fetch, "fetch directory");
    }

    /// <summary>
    /// Bash path containment compares strings, so MSYS POSIX paths such as
    /// <c>/c/...</c> are never reinterpreted as Win32 paths.
    /// </summary>
    [Fact]
    public void BashDirectoryContainment_ComparesPosixPathsWithoutWin32Normalization()
    {
        const string tmpDir = "/c/Users/runneradmin/AppData/Local/Temp/pb/scaffold-tmpdir-abc/tmp-sentinel";
        Assert.True(IsUnderBashDirectory(tmpDir + "/plutonium-init.12345678", tmpDir));
        Assert.True(IsUnderBashDirectory(tmpDir + "/plutonium-init.12345678", tmpDir + "/"));
        Assert.False(IsUnderBashDirectory("/tmp/plutonium-init.12345678", tmpDir));
        Assert.False(IsUnderBashDirectory(tmpDir, tmpDir));
        Assert.False(IsUnderBashDirectory(tmpDir + "-sibling/plutonium-init.12345678", tmpDir));
        Assert.False(IsUnderBashDirectory(@"C:\Temp\sentinel\plutonium-init.1", @"C:\Temp\sentinel"));
    }

    private static IEnumerable<string> EnumerateScaffoldScripts(string root)
    {
        yield return Path.Combine(root, "init.sh");
        yield return Path.Combine(root, "build.sh");
        foreach (var script in Directory.GetFiles(
                     Path.Combine(root, "scaffold"),
                     "*.sh",
                     SearchOption.AllDirectories))
        {
            yield return script;
        }
    }

    private static async Task<string> ResolveGitPathAsync(TempWorkspace workspace)
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

    private static async Task<string> ReadSingleLineAsync(string path, string description)
    {
        Assert.True(File.Exists(path), "Missing " + description + ": " + path);
        var lines = (await File.ReadAllLinesAsync(path))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        Assert.True(lines.Length == 1, description + " must contain exactly one path: " + path);
        return lines[0];
    }

    private static bool IsUnderBashDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.TrimEnd('/');
        return normalizedDirectory.Length > 0 &&
               path.Length > normalizedDirectory.Length &&
               path.StartsWith(normalizedDirectory + "/", StringComparison.Ordinal);
    }

    private static async Task AssertBashPathExistsAsync(
        TempWorkspace workspace,
        string path,
        string description)
    {
        var result = await RunBashPathProbeAsync(workspace, "exists", path);
        Assert.True(
            result.Succeeded,
            description + " does not exist: " + path + Environment.NewLine + result.Describe());
    }

    private static async Task AssertBashPathMissingAsync(
        TempWorkspace workspace,
        string path,
        string description)
    {
        var result = await RunBashPathProbeAsync(workspace, "absent", path);
        Assert.True(
            result.Succeeded,
            description + " still exists: " + path + Environment.NewLine + result.Describe());
    }

    private static async Task<ProcessResult> RunBashPathProbeAsync(
        TempWorkspace workspace,
        string mode,
        string path)
    {
        var probePath = workspace.PathFor("path-probe.sh");
        await File.WriteAllTextAsync(
            probePath,
            """
            #!/usr/bin/env bash
            set -euo pipefail
            mode=$1
            path=$2
            case "$mode" in
              exists) test -e "$path" ;;
              absent) test ! -e "$path" ;;
              *) exit 2 ;;
            esac
            """);
        return await ProcessHarness.RunBashAsync(probePath, [mode, path], workspace.Root);
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string ToBashPath(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
}
