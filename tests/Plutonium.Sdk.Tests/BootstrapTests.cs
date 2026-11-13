using Plutonium.Testing;
using Xunit;

namespace Plutonium.Sdk.Tests;

/// <summary>
/// Pins SDK bootstrap behavior: no download when <c>dotnet</c> is usable,
/// checksum verification before execution, actionable offline failures, exit
/// code propagation, and no writes outside <c>build/temp</c>.
/// </summary>
public sealed class BootstrapTests
{
    /// <summary>A usable dotnet on PATH skips the download path entirely.</summary>
    [Fact]
    public async Task ExistingDotNet_SkipsDownload()
    {
        using var workspace = TempWorkspace.Create("sdk-existing");
        var layout = RepositoryLayout.Locate();
        var scaffoldRoot = BootstrapTestContext.CopyScaffold(workspace, layout);
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddDotNet(BootstrapTestContext.SdkVersion, 0);
        toolchain.AddCurl("fail");

        var result = await BootstrapTestContext.RunBuildScriptAsync(scaffoldRoot, toolchain);

        AssertSucceeded(result, "build with an existing dotnet");
        Assert.DoesNotContain("curl", toolchain.ReadLog(), StringComparison.Ordinal);
        Assert.False(
            BootstrapTestContext.HasInstalledDotNet(scaffoldRoot),
            "An existing dotnet should not trigger an SDK install.");
        Assert.Contains("dotnet ", toolchain.ReadLog(), StringComparison.Ordinal);
        BootstrapTestContext.AssertNoHomeDotNet(toolchain);

    }

    /// <summary>Bootstrap installs the SDK under build/temp, never under HOME.</summary>
    [Fact]
    public async Task Bootstrap_InstallsSdkUnderBuildTemp()
    {
        using var workspace = TempWorkspace.Create("sdk-install");
        var layout = RepositoryLayout.Locate();
        var scaffoldRoot = BootstrapTestContext.CopyScaffold(workspace, layout);
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddCurl("copy");

        var payload = BootstrapTestContext.WriteFakeInstaller(
            workspace,
            toolchain,
            BootstrapTestContext.SdkVersion);
        PatchInstallerChecksum(scaffoldRoot, payload);

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FAKE_CURL_PAYLOAD"] = payload,
        };
        var result = await BootstrapTestContext.RunBuildScriptAsync(scaffoldRoot, toolchain, environment);

        AssertSucceeded(result, "build with a bootstrap install");
        Assert.True(
            BootstrapTestContext.HasInstalledDotNet(scaffoldRoot),
            "Bootstrap did not install dotnet under build/temp." + Environment.NewLine + result.Describe());
        Assert.True(
            File.Exists(workspace.PathFor("payload", "installer-executed")),
            "The verified installer did not run.");
        Assert.Contains("dotnet ", toolchain.ReadLog(), StringComparison.Ordinal);

        var tempDirectory = Path.Combine(scaffoldRoot, "build", "temp");
        var installed = Directory.GetFiles(tempDirectory, "dotnet", SearchOption.AllDirectories);
        Assert.All(
            installed,
            path => Assert.StartsWith(
                Path.GetFullPath(tempDirectory),
                Path.GetFullPath(path),
                StringComparison.Ordinal));
        BootstrapTestContext.AssertNoHomeDotNet(toolchain);

    }

    /// <summary>A tampered installer fails checksum verification before it can run.</summary>
    [Fact]
    public async Task ChecksumMismatch_StopsBeforeExecution()
    {
        using var workspace = TempWorkspace.Create("sdk-checksum");
        var layout = RepositoryLayout.Locate();
        var scaffoldRoot = BootstrapTestContext.CopyScaffold(workspace, layout);
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddCurl("tamper");

        var result = await BootstrapTestContext.RunBuildScriptAsync(scaffoldRoot, toolchain);

        AssertFailed(result, "build with a tampered installer");
        var output = result.StandardOutput + Environment.NewLine + result.StandardError;
        Assert.True(
            output.Contains("checksum", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("sha256", StringComparison.OrdinalIgnoreCase),
            "Checksum failure should be explicit." + Environment.NewLine + result.Describe());
        Assert.False(
            File.Exists(workspace.PathFor("payload", "installer-executed")),
            "A tampered installer was executed.");
        Assert.False(
            BootstrapTestContext.HasInstalledDotNet(scaffoldRoot),
            "A tampered installer produced an SDK.");

    }

    /// <summary>An offline download failure names the SDK and the failed download.</summary>
    [Fact]
    public async Task OfflineFailure_IsActionable()
    {
        using var workspace = TempWorkspace.Create("sdk-offline");
        var layout = RepositoryLayout.Locate();
        var scaffoldRoot = BootstrapTestContext.CopyScaffold(workspace, layout);
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddCurl("fail");

        var result = await BootstrapTestContext.RunBuildScriptAsync(scaffoldRoot, toolchain);

        AssertFailed(result, "offline bootstrap");
        var output = result.StandardOutput + Environment.NewLine + result.StandardError;
        Assert.Contains(BootstrapTestContext.SdkVersion, output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("download", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("curl", StringComparison.OrdinalIgnoreCase),
            "Offline failure should explain the download failure." + Environment.NewLine + result.Describe());
        Assert.False(BootstrapTestContext.HasInstalledDotNet(scaffoldRoot));

    }

    /// <summary>The build step's exit code is propagated unchanged.</summary>
    [Fact]
    public async Task BuildExitCode_Propagates()
    {
        using var workspace = TempWorkspace.Create("sdk-exit");
        var layout = RepositoryLayout.Locate();
        var scaffoldRoot = BootstrapTestContext.CopyScaffold(workspace, layout);
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddDotNet(BootstrapTestContext.SdkVersion, 37);
        toolchain.AddCurl("fail");

        var result = await BootstrapTestContext.RunBuildScriptAsync(scaffoldRoot, toolchain);

        Assert.False(result.TimedOut, "Build timed out." + Environment.NewLine + result.Describe());
        Assert.Equal(37, result.ExitCode);
        Assert.Contains("dotnet ", toolchain.ReadLog(), StringComparison.Ordinal);

    }

    /// <summary>A successful run leaves HOME and the managed scaffold files untouched.</summary>
    [Fact]
    public async Task SuccessfulRun_DoesNotPolluteHomeOrScaffold()
    {
        using var workspace = TempWorkspace.Create("sdk-clean");
        var layout = RepositoryLayout.Locate();
        var scaffoldRoot = BootstrapTestContext.CopyScaffold(workspace, layout);
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddDotNet(BootstrapTestContext.SdkVersion, 0);
        toolchain.AddCurl("fail");

        var before = DirectorySnapshot.Capture(
            Path.Combine(scaffoldRoot, "build"),
            "temp",
            "bin",
            "obj");
        var result = await BootstrapTestContext.RunBuildScriptAsync(scaffoldRoot, toolchain);

        AssertSucceeded(result, "clean build");
        var after = DirectorySnapshot.Capture(
            Path.Combine(scaffoldRoot, "build"),
            "temp",
            "bin",
            "obj");
        var differences = before.Differences(after);
        Assert.True(differences.Count == 0, "Bootstrap changed managed files: " + string.Join(", ", differences));
        BootstrapTestContext.AssertNoHomeDotNet(toolchain);

    }

    private static void PatchInstallerChecksum(string scaffoldRoot, string payloadPath)
    {
        var lockPath = Path.Combine(scaffoldRoot, "build", "plutonium.lock");
        Assert.True(File.Exists(lockPath), "Lock file not found: " + lockPath);
        var lockFile = PlutoniumLockFile.Load(lockPath);
        var updated = lockFile.ReplaceHashesForMarker("install", Hashing.Sha256File(payloadPath));
        Assert.True(updated > 0, "No installer checksum line found in the lock.");
        lockFile.Save();
    }

    private static void AssertSucceeded(ProcessResult result, string operation)
    {
        Assert.True(result.Succeeded, operation + " failed." + Environment.NewLine + result.Describe());
    }

    private static void AssertFailed(ProcessResult result, string operation)
    {
        Assert.False(result.Succeeded, operation + " unexpectedly succeeded." + Environment.NewLine + result.Describe());
    }
}
