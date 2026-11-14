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
    /// A backslash ref reaches the script unchanged through the real shell, so
    /// the malformed-ref grammar is not rewritten into a valid ref on Windows.
    /// </summary>
    [Fact]
    public async Task LiteralArguments_ReachTheScriptUnchanged()
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

        var literal = @"refs/heads/ma\in";
        var result = await ProcessHarness.RunBashAsync(
            probe,
            [literal],
            literalArgumentIndexes: [0]);

        Assert.True(result.Succeeded, result.Describe());
        Assert.Equal(literal, result.StandardOutput);
    }
}
