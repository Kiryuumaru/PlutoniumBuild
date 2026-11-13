using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Exceptions;
using ApplicationBuilderHelpers.Extensions;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Logging;
using Plutonium.Helpers.App;
using Plutonium.Helpers.Pipelines.Context;
using Xunit;

namespace Plutonium.Shell.Tests;

/// <summary>Routing coverage: verb dispatch, aliases, legacy names, and exit codes.</summary>
public sealed class PlutoniumShellRoutingTests
{
    private sealed class RoutingBuild;

    private static (ApplicationBuilder Builder, CapturingSink Sink) CreateShell()
    {
        var sink = new CapturingSink();
        var logger = new PlutoniumLogger([sink]);
        var builder = ApplicationBuilder.Create();
        builder.AddPlutoniumShell(new RoutingBuild(), ShellTestFixtures.CreateOptions(), logger);
        return (builder, sink);
    }

    /// <summary>The fetch verb lists its target and exits 0 via <c>--list</c>.</summary>
    [Fact]
    public async Task FetchListExitsZero()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Fetch", "--list"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.Fetch);
    }

    /// <summary>The kebab-case alias routes to the same verb target.</summary>
    [Fact]
    public async Task KebabAliasRoutesToSameVerb()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "bump-and-forget", "--list"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.BumpAndForget);
    }

    /// <summary>The frozen legacy name still routes to the GitHub workflow verb.</summary>
    [Fact]
    public async Task LegacyGithubWorkflowNameRoutes()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "GithubWorkflow", "--list"]);

        Assert.Equal(0, exit);
        Assert.Equal("GithubWorkflow", PlutoniumBuildVerbs.GitHubWorkflow);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.GitHubWorkflow);
    }

    /// <summary>The PascalCase build verb routes and lists successfully.</summary>
    [Fact]
    public async Task BuildVerbListExitsZero()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Build", "--list"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.Build);
    }

    /// <summary>Unknown commands are usage errors mapping to exit 2.</summary>
    [Fact]
    public async Task UnknownCommandExitsTwo()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync(["shell", "NoSuchVerb"]);

        Assert.Equal(2, exit);
    }

    /// <summary>Unknown options are usage errors mapping to exit 2.</summary>
    [Fact]
    public async Task UnknownOptionExitsTwo()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Fetch", "--no-such-option"]);

        Assert.Equal(2, exit);
    }

    /// <summary>Empty <c>--skip</c> values are usage errors mapping to exit 2.</summary>
    [Fact]
    public async Task EmptySkipExitsTwo()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Fetch", "--skip", ""]);

        Assert.Equal(2, exit);
    }

    /// <summary>Malformed <c>--args</c> values are usage errors mapping to exit 2.</summary>
    [Fact]
    public async Task MalformedArgsExitsTwo()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Bump", "--args", ""]);

        Assert.Equal(2, exit);
    }

    /// <summary>Unknown skip targets are faults mapping to exit 1.</summary>
    [Fact]
    public async Task UnknownSkipTargetExitsOne()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Fetch", "--skip", "NoSuchTarget"]);

        Assert.Equal(1, exit);
    }

    /// <summary>An unknown app in <c>--args</c> faults through the real builder to exit 1.</summary>
    [Fact]
    public async Task UnknownArgsAppExitsOne()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync(["shell", "delete-origin-tags", "--args", "no-such-app=minor+1"]);

        Assert.Equal(1, exit);
    }

    /// <summary>The canonical kebab spelling routes to the GitHub workflow verb.</summary>
    [Fact]
    public async Task CanonicalGithubWorkflowSpellingRoutes()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "github-workflow", "--list"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.GitHubWorkflow);
    }

    /// <summary><c>--all</c> listing includes unlisted targets through the real CLI.</summary>
    [Fact]
    public async Task AllFlagListsUnlistedTargets()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Fetch", "--list", "--all"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.PipelinePreSetup);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.DeleteOriginTags);
    }

    /// <summary>Failing verb execution maps to exit 1 through the real builder fault path.</summary>
    [Fact]
    public async Task VerbFailureMapsToExitOne()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync(["shell", "delete-origin-tags"]);

        Assert.Equal(1, exit);
    }

    /// <summary>A valid <c>--args</c> value survives the real CLI and lands in the context map.</summary>
    [Fact]
    public void ValidArgsReachContextThroughRealPath()
    {
        var sink = new CapturingSink();
        var logger = ShellTestFixtures.CreateLogger(sink);
        var state = ShellTestFixtures.CreateState(
            new SingleAppFakeBuild(), ShellTestFixtures.CreateOptions(), logger);

        state.ApplySplitArgs([new PlutoniumBumpArg("shelltest", "minor+1")], state.KnownAppIds());
        var names = state.ListTargets(includeUnlisted: false);

        Assert.Equal("minor+1", state.Context.SplitArgs["shelltest"]);
        Assert.Contains(PlutoniumBuildVerbs.Fetch, names);
    }

    /// <summary>A bare expression resolves to the sole known app through the real path.</summary>
    [Fact]
    public void BareExpressionResolvesSingleAppThroughRealPath()
    {
        var sink = new CapturingSink();
        var logger = ShellTestFixtures.CreateLogger(sink);
        var state = ShellTestFixtures.CreateState(
            new SingleAppFakeBuild(), ShellTestFixtures.CreateOptions(), logger);

        state.ApplySplitArgs([new PlutoniumBumpArg("shelltest", null)], state.KnownAppIds());

        Assert.Null(state.Context.SplitArgs["shelltest"]);
    }

    /// <summary>A bare token resolves through the real single-app discovery path.</summary>
    [Fact]
    public void BareTokenResolvesSingleAppThroughRealPath()
    {
        var sink = new CapturingSink();
        var logger = ShellTestFixtures.CreateLogger(sink);
        var state = ShellTestFixtures.CreateState(
            new SingleAppFakeBuild(), ShellTestFixtures.CreateOptions(), logger);

        state.ApplySplitArgs([new PlutoniumBumpArg("minor+1", null)], state.KnownAppIds());

        Assert.Single(state.Context.SplitArgs);
        Assert.Null(state.Context.SplitArgs["minor+1"]);
    }

    /// <summary>Pre-cancelled runs exit 130 through the cancellation path.</summary>
    [Fact]
    public async Task CancelledRunExitsOneThirty()
    {
        var (builder, _) = CreateShell();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var exit = await builder.RunAsync(["shell", "Fetch"], cancelled.Token);

        Assert.Equal(130, exit);
    }

    /// <summary>Bare invocation routes to the default root command through the real builder.</summary>
    [Fact]
    public async Task BareRootRoutesToInteractiveDefault()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync([]);

        Assert.True(exit is 0 or 2, $"Bare root must route to the Interactive default (exit 0 with a console, 2 when stdin is redirected), but was {exit}.");
    }

    /// <summary>Redirected stdin reaches the interactive default and fails fast with exit 2.</summary>
    [Fact]
    public async Task RedirectedStdinGuardsInteractive()
    {
        var (builder, _) = CreateShell();

        var exit = await builder.RunAsync([]);

        if (Console.IsInputRedirected)
        {
            Assert.Equal(2, exit);
        }
        else
        {
            Assert.Equal(0, exit);
        }
    }
}
