using Plutonium.Testing;
using Xunit;

namespace Plutonium.Sdk.Tests;

/// <summary>
/// Pins the line endings of generated shell scripts: Git Bash does not strip
/// carriage returns, so every shim and installer payload must be written with LF.
/// </summary>
public sealed class GeneratedScriptLineEndingTests
{
    /// <summary>Generated shims contain no carriage returns, even for a CRLF body.</summary>
    [Fact]
    public void Shims_UseLfLineEndings()
    {
        using var workspace = TempWorkspace.Create("sdk-shim-line-endings");
        var toolchain = FakeToolchain.Create(workspace);
        toolchain.AddDotNet(BootstrapTestContext.SdkVersion, 0);
        toolchain.AddCurl("fail");
        toolchain.AddShim("uname", "#!/usr/bin/env bash\r\nprintf 'Linux\\n'\r\n");

        var shims = Directory.GetFiles(toolchain.ShimDirectory, "*", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(shims);
        Assert.All(
            shims,
            path => Assert.DoesNotContain("\r", File.ReadAllText(path), StringComparison.Ordinal));
    }

    /// <summary>The fake installer payload contains no carriage returns.</summary>
    [Fact]
    public void FakeInstallerPayload_UsesLfLineEndings()
    {
        using var workspace = TempWorkspace.Create("sdk-payload-line-endings");
        var toolchain = FakeToolchain.Create(workspace);

        var payload = BootstrapTestContext.WriteFakeInstaller(
            workspace,
            toolchain,
            BootstrapTestContext.SdkVersion);

        Assert.DoesNotContain("\r", File.ReadAllText(payload), StringComparison.Ordinal);
    }
}
