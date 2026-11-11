using Plutonium.Testing;
using Xunit;

namespace Plutonium.Sdk.Tests;

/// <summary>
/// Pins the fake toolchain harness: shims must be executable by the bash that
/// runs them, and the shim directory must win PATH resolution ahead of the
/// host's real tools.
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
}
