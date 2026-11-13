using Plutonium.Testing;
using Xunit;

namespace Plutonium.Sdk.Tests;

/// <summary>
/// Pins the fake toolchain harness: shims must be executable by the bash that
/// runs them, the shim directory must win PATH resolution ahead of the host's
/// real tools, and an inherited host PATH must put Git's POSIX tools ahead of
/// the Windows system tools that shadow them.
/// </summary>
public sealed class FakeToolchainTests
{
    /// <summary>
    /// Shims are executable and resolve ahead of the bash tool directories,
    /// with or without the host PATH appended.
    /// </summary>
    [Fact]
    public async Task Shims_AreExecutableAndFirstOnPath()
    {
        using var workspace = TempWorkspace.Create("sdk-fake-toolchain");
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddShim("uname", "#!/usr/bin/env bash\nprintf 'Linux\\n'\n");

        var shim = Path.Combine(toolchain.ShimDirectory, "uname");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(shim);
            Assert.True(
                (mode & UnixFileMode.UserExecute) != 0,
                "The Unix shim is missing the user execute bit: " + mode);
        }

        var probe = Path.Combine(workspace.Root, "probe.sh");
        var probeContent = """
            #!/usr/bin/env bash
            set -euo pipefail
            shim_directory=$1
            required_tool=$2
            expected=$(cd "$shim_directory" && pwd -P)
            first=${PATH%%:*}
            first_directory=$(cd "$first" && pwd -P)
            if [ "$first_directory" != "$expected" ]; then
              printf 'PATH starts with %s (%s), not %s\n' "$first" "$first_directory" "$expected" >&2
              exit 3
            fi
            resolved=$(command -v uname || true)
            if [ -z "$resolved" ]; then
              printf 'uname is not on PATH\n' >&2
              exit 4
            fi
            resolved_directory=$(cd "${resolved%/*}" && pwd -P)
            if [ "$resolved_directory" != "$expected" ] || [ "${resolved##*/}" != "uname" ]; then
              printf 'uname resolved to %s, not %s/uname\n' "$resolved" "$expected" >&2
              exit 5
            fi
            test -x "$resolved"
            if ! command -v "$required_tool" >/dev/null 2>&1; then
              printf '%s is not on PATH\n' "$required_tool" >&2
              exit 6
            fi
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(probe, probeContent);
        Assert.DoesNotContain("\r", File.ReadAllText(probe), StringComparison.Ordinal);

        var fullPath = await ProcessHarness.RunBashAsync(
            probe,
            [toolchain.ShimDirectory, "chmod"],
            environment: toolchain.BuildEnvironment());

        Assert.True(fullPath.Succeeded, fullPath.Describe());

        var isolatedPath = await ProcessHarness.RunBashAsync(
            probe,
            [toolchain.ShimDirectory, "chmod"],
            environment: toolchain.BuildEnvironment(includeExistingPath: false));

        Assert.True(isolatedPath.Succeeded, isolatedPath.Describe());
    }

    /// <summary>
    /// A bash script invoked without an explicit PATH still resolves the POSIX
    /// tools Git for Windows keeps in <c>usr\bin</c>, including the tools that
    /// the Windows system directories shadow by name. The compat wrapper used
    /// to supply them; the real shell does not, so the harness must put them
    /// ahead of the inherited host PATH.
    /// </summary>
    [Fact]
    public async Task BashScripts_ResolvePosixToolsWithoutAPathOverride()
    {
        using var workspace = TempWorkspace.Create("sdk-bash-tools");
        var probe = Path.Combine(workspace.Root, "tools.sh");
        File.WriteAllText(
            probe,
            """
            #!/usr/bin/env bash
            set -euo pipefail
            command -v awk >/dev/null
            command -v sed >/dev/null
            command -v mktemp >/dev/null
            for tool in find sort; do
              resolved=$(command -v "$tool" || true)
              if [ -z "$resolved" ]; then
                printf '%s is not on PATH\n' "$tool" >&2
                exit 7
              fi
              directory=$(cd "$(dirname "$resolved")" && pwd -P | tr '[:upper:]' '[:lower:]')
              case "$directory" in
                */windows/system32|*/windows/sysnative)
                  printf '%s resolved to the Windows system tool: %s\n' "$tool" "$resolved" >&2
                  exit 8
                  ;;
              esac
            done
            if ! find --version </dev/null >/dev/null 2>&1; then
              find "$1" -name 'plutonium-nonexistent-probe' </dev/null >/dev/null
            fi
            if ! sort --version </dev/null >/dev/null 2>&1; then
              printf 'b\na\n' | sort >/dev/null
            fi
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.DoesNotContain("\r", File.ReadAllText(probe), StringComparison.Ordinal);

        var result = await ProcessHarness.RunBashAsync(probe, [workspace.Root]);

        Assert.True(result.Succeeded, result.Describe());
    }

    /// <summary>
    /// An inherited host PATH must put the Git tool directories first so the
    /// Windows system tools that share names with POSIX tools (<c>find.exe</c>,
    /// <c>sort.exe</c>) cannot shadow them.
    /// </summary>
    [Fact]
    public void InheritedPath_PutsGitToolsBeforeSystemDirectories()
    {
        var tools = new[] { "git/usr/bin", "git/ucrt64/bin" };
        var hostEntries = new[] { "windows/System32", "windows" };

        var prepared = ProcessHarness.PrepareBashEnvironmentCore(
            tools,
            environment: null,
            inheritedPath: string.Join(Path.PathSeparator, hostEntries));

        Assert.NotNull(prepared);
        Assert.Equal(
            string.Join(Path.PathSeparator, tools.Concat(hostEntries)),
            prepared["PATH"]);
    }

    /// <summary>
    /// The toolchain environment isolates temporary files with <c>TMPDIR</c>
    /// only. <c>TMP</c> and <c>TEMP</c> are not exported because Git for
    /// Windows derives its shared MSYS <c>/tmp</c> mount from them, and that
    /// mount must not point at a disposable workspace. The guard runs on both
    /// the Unix and Windows test legs.
    /// </summary>
    [Fact]
    public void BuildEnvironment_ExportsTmpDirWithoutTmpOrTemp()
    {
        using var workspace = TempWorkspace.Create("sdk-toolchain-environment");
        var toolchain = FakeToolchain.Create(workspace);

        var environment = toolchain.BuildEnvironment();

        Assert.True(environment.TryGetValue("TMPDIR", out var tmpDir), "TMPDIR is missing.");
        Assert.Equal(toolchain.TempDirectory, tmpDir);
        Assert.True(
            Directory.Exists(toolchain.TempDirectory),
            "The TMPDIR directory was not created: " + toolchain.TempDirectory);
        Assert.False(environment.ContainsKey("TMP"), "TMP must not be exported.");
        Assert.False(environment.ContainsKey("TEMP"), "TEMP must not be exported.");
    }

    /// <summary>
    /// A caller-supplied PATH keeps its entries first so a shim directory still
    /// wins, and the Git tool directories are appended after it.
    /// </summary>
    [Fact]
    public void CallerPath_KeepsShimDirectoryBeforeGitTools()
    {
        var tools = new[] { "git/usr/bin", "git/ucrt64/bin" };
        var callerEntries = new[] { "toolchain/bin", "windows/System32" };

        var prepared = ProcessHarness.PrepareBashEnvironmentCore(
            tools,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = string.Join(Path.PathSeparator, callerEntries),
            },
            inheritedPath: "windows/System32");

        Assert.NotNull(prepared);
        Assert.Equal(
            string.Join(Path.PathSeparator, callerEntries.Concat(tools)),
            prepared["PATH"]);
    }
}
