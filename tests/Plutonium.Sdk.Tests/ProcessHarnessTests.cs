using Plutonium.Testing;
using Xunit;

namespace Plutonium.Sdk.Tests;

/// <summary>
/// Pins the bash argument channel: path-shaped values are normalized for the
/// host, while literal grammar tokens such as ref names reach the script
/// unchanged so a malformed ref is tested exactly as written.
/// </summary>
public sealed class ProcessHarnessTests
{
    /// <summary>
    /// A literal argument bypasses path normalization while its neighbors keep
    /// the platform path mapping.
    /// </summary>
    [Fact]
    public void LiteralArguments_AreNotNormalizedAsPaths()
    {
        var arguments = new[] { "--ref", @"refs/heads/ma\in", "--target", @"C:\consumer" };

        var normalized = ProcessHarness.NormalizeBashArguments(arguments, [1]);

        Assert.Equal(@"refs/heads/ma\in", normalized[1]);
        Assert.Equal(ProcessHarness.ToBashPath(@"C:\consumer"), normalized[3]);
    }

    /// <summary>
    /// A literal argument with path or glob punctuation reaches the script
    /// unchanged through the real shell, so the malformed-ref grammar is not
    /// rewritten into a valid ref on Windows.
    /// </summary>
    /// <param name="literal">The literal argument value under test.</param>
    [Theory]
    [InlineData(@"refs/heads/ma\in")]
    [InlineData("refs/heads/ma@{in")]
    public async Task LiteralArguments_ReachTheScriptUnchanged(string literal)
    {
        using var workspace = TempWorkspace.Create("sdk-literal-arguments");
        var probe = Path.Combine(workspace.Root, "echo-argument.sh");
        await File.WriteAllTextAsync(
            probe,
            """
            #!/usr/bin/env bash
            set -euo pipefail
            printf '%s' "$1"
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.DoesNotContain("\r", await File.ReadAllTextAsync(probe), StringComparison.Ordinal);

        var result = await ProcessHarness.RunBashAsync(
            probe,
            [literal],
            literalArgumentIndexes: [0]);

        Assert.True(result.Succeeded, result.Describe());
        Assert.Equal(literal, result.StandardOutput);
    }

    /// <summary>An existing MSYS option string is preserved and extended with noglob.</summary>
    [Fact]
    public void MsysNoGlob_PreservesExistingOptions()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MSYS"] = "winsymlinks:nativestrict",
        };

        var merged = ProcessHarness.MergeMsysNoGlob(environment, inheritedMsys: "glob:ignorecase");

        Assert.Equal("winsymlinks:nativestrict noglob", merged["MSYS"]);
        Assert.Equal("winsymlinks:nativestrict", environment["MSYS"]);
    }

    /// <summary>An inherited MSYS option string is used when the caller supplies none.</summary>
    [Fact]
    public void MsysNoGlob_UsesInheritedOptionsWhenTheCallerSuppliesNone()
    {
        var merged = ProcessHarness.MergeMsysNoGlob(environment: null, inheritedMsys: "glob:ignorecase");

        Assert.Equal("glob:ignorecase noglob", merged["MSYS"]);
    }

    /// <summary>A caller-removed MSYS value does not resurrect the inherited options.</summary>
    [Fact]
    public void MsysNoGlob_ReplacesARemovedValue()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MSYS"] = null,
        };

        var merged = ProcessHarness.MergeMsysNoGlob(environment, inheritedMsys: "glob:ignorecase");

        Assert.Equal("noglob", merged["MSYS"]);
    }
}
