using ApplicationBuilderHelpers.Exceptions;
using Plutonium.Core.Logging;
using Plutonium.Helpers.App;
using Xunit;

namespace Plutonium.Shell.Tests;

/// <summary>State coverage: split-args mapping, verb routing via <c>--list</c>, and exit codes.</summary>
public sealed class PlutoniumShellStateTests : IDisposable
{
    private const string SecretVariable = "PLUTONIUM_SHELL_TEST_SECRET";

    private readonly CapturingSink _sink = new();
    private readonly string? _priorSecret;
    private bool _disposed;

    /// <summary>Initializes captured environment state for secret tests.</summary>
    public PlutoniumShellStateTests()
    {
        _priorSecret = Environment.GetEnvironmentVariable(SecretVariable);
    }

    /// <summary>Restores the secret variable to its prior value.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Environment.SetEnvironmentVariable(SecretVariable, _priorSecret);
    }

    private sealed class PlainBuild;

    /// <summary>Keyed values land in the context split-args map verbatim.</summary>
    [Fact]
    public void ApplySplitArgsStoresKeyedValues()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);

        state.ApplySplitArgs([new PlutoniumBumpArg("myapp", "minor+1")], ["myapp"]);

        Assert.Equal("minor+1", state.Context.SplitArgs["myapp"]);
    }

    /// <summary>Unknown bare expressions are usage errors mapping to exit 2.</summary>
    [Fact]
    public void BareExpressionWithMultipleAppsIsUsageError()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);

        var error = Assert.Throws<CommandException>(() =>
            state.ApplySplitArgs([new PlutoniumBumpArg("minor+1", null)], ["app-a", "app-b"]));

        Assert.Equal(2, error.ExitCode);
        Assert.Equal(CommandErrorKind.InvalidValue, error.Kind);
    }

    /// <summary>Unknown verb targets are faults mapping to exit 1.</summary>
    [Fact]
    public async Task UnknownVerbIsFaultExitOne()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);

        var error = await Assert.ThrowsAsync<CommandException>(() =>
            state.RunVerbAsync("NoSuchVerb", [], CancellationToken.None));

        Assert.Equal(1, error.ExitCode);
        Assert.Equal(CommandErrorKind.Fault, error.Kind);
    }

    /// <summary>Pre-cancelled tokens propagate cancellation for the 130 path.</summary>
    [Fact]
    public async Task CancelledTokenPropagatesCancellation()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            state.RunVerbAsync(PlutoniumBuildVerbs.Fetch, [], cancelled.Token));
    }

    /// <summary>A bare expression resolves through the downstream single-app rule.</summary>
    [Fact]
    public void BareExpressionRuleAcceptsSingleAppForm()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);

        state.ApplySplitArgs([new PlutoniumBumpArg("shelltest", null)], ["shelltest"]);

        Assert.Null(state.Context.SplitArgs["shelltest"]);
    }

    /// <summary>A bare token with exactly one known app binds as the single-app shorthand.</summary>
    [Fact]
    public void BareTokenWithSingleAppBindsAsShorthand()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);

        state.ApplySplitArgs([new PlutoniumBumpArg("minor+1", null)], ["shelltest"]);

        Assert.Single(state.Context.SplitArgs);
        Assert.Null(state.Context.SplitArgs["minor+1"]);
    }

    /// <summary>An empty direct-API expression is rejected downstream when parsing bumps.</summary>
    [Fact]
    public void EmptyBareExpressionRejectedDownstream()
    {
        Assert.Throws<ArgumentException>(() =>
            Plutonium.Helpers.Versioning.PlutoniumBumpArgs.ParseMap(
                new Dictionary<string, string?>(StringComparer.Ordinal) { [string.Empty] = null },
                ["shelltest"]));
    }

    /// <summary><c>--list</c> reaches the runner and logs every listed target.</summary>
    [Fact]
    public void ListTargetsLogsListedNames()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);

        var names = state.ListTargets(includeUnlisted: false);

        Assert.Contains(PlutoniumBuildVerbs.Fetch, names);
        Assert.Contains(PlutoniumBuildVerbs.Version, names);
        Assert.DoesNotContain(PlutoniumBuildVerbs.PipelinePreSetup, names);
        Assert.Contains(
            _sink.Entries,
            entry => entry.Message == PlutoniumBuildVerbs.Fetch && entry.Level == PlutoniumLogLevel.Information);
    }

    /// <summary><c>--all</c> includes the unlisted pipeline verbs in the listing.</summary>
    [Fact]
    public void ListTargetsWithAllIncludesUnlisted()
    {
        var logger = ShellTestFixtures.CreateLogger(_sink);
        var state = ShellTestFixtures.CreateState(new PlainBuild(), ShellTestFixtures.CreateOptions(), logger);

        var names = state.ListTargets(includeUnlisted: true);

        Assert.Contains(PlutoniumBuildVerbs.PipelinePreSetup, names);
        Assert.Contains(PlutoniumBuildVerbs.DeleteOriginTags, names);
    }

    /// <summary>Registered secrets never appear in captured log output.</summary>
    [Fact]
    public void RegisteredSecretNeverAppearsInLogOutput()
    {
        const string secret = "shell-test-secret-value-9f3a";
        Environment.SetEnvironmentVariable(SecretVariable, secret);
        var logger = ShellTestFixtures.CreateLogger(_sink);
        ShellTestFixtures.CreateState(new SecretFakeBuild(), ShellTestFixtures.CreateOptions(), logger);

        logger.Log(PlutoniumLogLevel.Information, $"token is {secret} here");

        foreach (var entry in _sink.Entries)
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
        }

        Assert.Contains(_sink.Entries, entry => entry.Message.Contains("***", StringComparison.Ordinal));
    }
}
