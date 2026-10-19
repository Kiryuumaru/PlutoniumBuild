using Plutonium.Core.FileSystem;
using Plutonium.Core.Logging;
using Plutonium.Helpers.App;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Pipelines.Context;

namespace Plutonium.Shell.Tests;

internal sealed class CapturingSink : IPlutoniumLogSink
{
    public List<(PlutoniumLogLevel Level, string? Scope, string Message)> Entries { get; } = [];

    public void Write(PlutoniumLogLevel level, string? scope, string message) =>
        Entries.Add((level, scope, message));
}

internal sealed class SecretFakeBuild
{
    [PlutoniumSecretVariable("ShellTest", "PLUTONIUM_SHELL_TEST_SECRET")]
    public string? Token { get; set; }
}

/// <summary>Single-app fake build for <c>--args</c> end-to-end coverage.</summary>
internal sealed class SingleAppFakeBuild
{
    /// <summary>Gets the workflow configuration delegate discovery requires.</summary>
    public WorkflowConfigEntry WorkflowConfig => definition => definition;

    /// <summary>Gets the single known-app entry keyed to <c>shelltest</c>.</summary>
    public TestEntry ShellTest => definition => definition.AppId("shelltest");
}

internal static class ShellTestFixtures
{
    public static PlutoniumBuildOptions CreateOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "plutonium-shell-tests");
        return new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath(root),
            new PlutoniumPath(Path.Combine(root, "tmp")),
            new PlutoniumPath(Path.Combine(root, "out")),
            new PlutoniumPath(Path.Combine(root, "cache")),
            "https://example.com/org/repo");
    }

    public static PlutoniumLogger CreateLogger(CapturingSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        return new PlutoniumLogger([sink]);
    }

    public static PlutoniumShellState CreateState(object build, PlutoniumBuildOptions options, PlutoniumLogger logger)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        return PlutoniumShellState.Create(build, options, logger);
    }
}
