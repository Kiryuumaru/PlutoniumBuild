using System.Security;
using Plutonium.Testing;
using Xunit;

namespace Plutonium.Sdk.Tests;

/// <summary>Builds disposable scaffold copies and fake installer payloads.</summary>
internal static class BootstrapTestContext
{
    internal const string SdkVersion = "10.0.402";

    private const string UnameShim = """
        case "${1:-}" in
          -s) printf 'Linux\n' ;;
          -m) printf 'x86_64\n' ;;
          *) printf 'Linux\n' ;;
        esac
        """;

    internal static string CopyScaffold(TempWorkspace workspace, RepositoryLayout layout)
    {
        if (!Directory.Exists(layout.ScaffoldBuildDirectory))
        {
            throw new InvalidOperationException(
                "Scaffold payload not found at '" + layout.ScaffoldBuildDirectory + "'.");
        }

        var root = workspace.PathFor("scaffold");
        CopyTree(layout.ScaffoldBuildDirectory, Path.Combine(root, "build"));
        WriteNuGetConfig(workspace, root);
        return root;
    }

    internal static Task<ProcessResult> RunBuildScriptAsync(
        string scaffoldRoot,
        FakeToolchain toolchain,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null)
    {
        var script = Path.Combine(scaffoldRoot, "build", "build.sh");
        Assert.True(File.Exists(script), "Scaffold build script not found: " + script);
        if (OperatingSystem.IsWindows())
        {
            // bootstrap.sh's installer path is intentionally POSIX-only. Git Bash
            // emulates it, so the platform probe is pinned to the Linux RID the
            // fake installer payload is written for.
            toolchain.AddShim("uname", UnameShim);
        }

        return ProcessHarness.RunBashAsync(
            script,
            [],
            scaffoldRoot,
            toolchain.BuildEnvironment(extraEnvironment, includeExistingPath: false),
            TimeSpan.FromMinutes(10));
    }

    private static void WriteNuGetConfig(TempWorkspace workspace, string scaffoldRoot)
    {
        var packages = workspace.PathFor("nuget", "packages").Replace('\\', '/');
        var content = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
              <config>
                <add key="globalPackagesFolder" value="PACKAGES" />
              </config>
            </configuration>
            """;
        content = content.Replace("PACKAGES", SecurityElement.Escape(packages), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(scaffoldRoot, "NuGet.Config"), content);
    }

    internal static string WriteFakeInstaller(
        TempWorkspace workspace,
        FakeToolchain toolchain,
        string sdkVersion)
    {
        var payloadDirectory = workspace.PathFor("payload");
        Directory.CreateDirectory(payloadDirectory);
        var payloadPath = Path.Combine(payloadDirectory, "dotnet-install.sh");
        var markerPath = Path.Combine(workspace.PathFor("payload"), "installer-executed");
        var content = """
            #!/usr/bin/env bash
            set -eu
            install_dir=""
            while [ "$#" -gt 0 ]; do
              case "$1" in
                --install-dir) install_dir="${2:-}"; shift 2 ;;
                --version|--channel) shift 2 ;;
                --no-path) shift ;;
                *) shift ;;
              esac
            done
            if [ -z "$install_dir" ]; then
              echo "fake installer: missing --install-dir" >&2
              exit 9
            fi
            mkdir -p "$install_dir"
            printf 'installed\n' > "MARKER"
            cat > "$install_dir/dotnet" <<'DOTNET_SCRIPT'
            #!/usr/bin/env bash
            printf 'dotnet %s\n' "$*" >> "LOG"
            case "${1:-}" in
              --version) printf '%s\n' "VERSION"; exit 0 ;;
              --list-sdks) printf '%s [%s]\n' "VERSION" "$(cd "$(dirname "$0")" && pwd)"; exit 0 ;;
              --info) printf 'Version: %s\n' "VERSION"; exit 0 ;;
            esac
            exit 0
            DOTNET_SCRIPT
            chmod +x "$install_dir/dotnet"
            """;

        content = content
            .Replace("MARKER", markerPath, StringComparison.Ordinal)
            .Replace("LOG", toolchain.LogPath, StringComparison.Ordinal)
            .Replace("VERSION", sdkVersion, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        File.WriteAllText(payloadPath, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                payloadPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return payloadPath;
    }

    internal static bool HasInstalledDotNet(string scaffoldRoot)
    {
        var temp = Path.Combine(scaffoldRoot, "build", "temp");
        return Directory.Exists(temp) &&
               Directory.GetFiles(temp, "dotnet", SearchOption.AllDirectories).Length > 0;
    }

    internal static void AssertNoHomeDotNet(FakeToolchain toolchain)
    {
        var home = toolchain.HomeDirectory;
        var files = Directory.Exists(home)
            ? Directory.GetFileSystemEntries(home, "*", SearchOption.AllDirectories)
            : [];
        Assert.Empty(files);
        Assert.False(Directory.Exists(Path.Combine(home, ".dotnet")));
        Assert.False(File.Exists(Path.Combine(home, ".bashrc")));
    }

    private static void CopyTree(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(
                    Path.GetFileName(directory),
                    ".git",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyTree(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)));
        }
    }
}
