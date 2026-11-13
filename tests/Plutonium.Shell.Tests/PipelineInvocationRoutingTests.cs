using System.Reflection;
using ApplicationBuilderHelpers;
using Plutonium.Core.Logging;
using Plutonium.Helpers.App;
using Plutonium.Helpers.Pipelines;
using Xunit;

namespace Plutonium.Shell.Tests;

/// <summary>
/// Routing coverage for the composed pipeline invocations: the shell-group kebab
/// commands emitted into generated workflow files route, their Pascal aliases route,
/// and the bare non-group spellings do not.
/// </summary>
public sealed class PipelineInvocationRoutingTests
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

    /// <summary>Verifies every pipeline command class binds the verb its spelling advertises.</summary>
    /// <param name="commandType">The command class under test.</param>
    /// <param name="expectedVerb">The target verb the class must bind.</param>
    [Theory]
    [InlineData(typeof(PlutoniumPipelinePreSetupCommand), PlutoniumBuildVerbs.PipelinePreSetup)]
    [InlineData(typeof(PlutoniumPipelinePreSetupAliasCommand), PlutoniumBuildVerbs.PipelinePreSetup)]
    [InlineData(typeof(PlutoniumPipelinePostSetupCommand), PlutoniumBuildVerbs.PipelinePostSetup)]
    [InlineData(typeof(PlutoniumPipelinePostSetupAliasCommand), PlutoniumBuildVerbs.PipelinePostSetup)]
    [InlineData(typeof(PlutoniumPipelineEntryRunCommand), PlutoniumBuildVerbs.PipelineEntryRun)]
    [InlineData(typeof(PlutoniumPipelineEntryRunAliasCommand), PlutoniumBuildVerbs.PipelineEntryRun)]
    public void PipelineCommandClasses_BindExpectedVerb(Type commandType, string expectedVerb)
    {
        Assert.Equal(expectedVerb, ResolveVerb(commandType));
    }

    /// <summary>Verifies the composed pre-setup invocation routes to the pre-setup verb.</summary>
    [Fact]
    public async Task ComposedPreSetupInvocation_RoutesToPreSetupVerb()
    {
        var runScript = "chmod +x ./build.sh && ./build.sh";
        var invocation = InvocationTokens(runScript, PlutoniumPipelineInvocation.ComposePreSetup(runScript));
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync([.. invocation, "--list", "--all"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.PipelinePreSetup);
    }

    /// <summary>Verifies the composed post-setup invocation routes to the post-setup verb.</summary>
    [Fact]
    public async Task ComposedPostSetupInvocation_RoutesToPostSetupVerb()
    {
        var runScript = "./build.cmd";
        var invocation = InvocationTokens(runScript, PlutoniumPipelineInvocation.ComposePostSetup(runScript));
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync([.. invocation, "--list", "--all"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.PipelinePostSetup);
    }

    /// <summary>Verifies the composed entry-run invocation routes to the entry-run verb.</summary>
    [Fact]
    public async Task ComposedEntryRunInvocation_RoutesToEntryRunVerb()
    {
        var runScript = "chmod +x ./build.sh && ./build.sh";
        var invocation = InvocationTokens(runScript, PlutoniumPipelineInvocation.ComposeEntryRun(runScript, "my-job"));
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync([.. invocation, "--list", "--all"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.PipelineEntryRun);
    }

    /// <summary>Verifies the Pascal aliases route under the shell group after the kebab primary.</summary>
    /// <param name="alias">The Pascal alias spelling.</param>
    [Theory]
    [InlineData("PipelinePreSetup")]
    [InlineData("PipelinePostSetup")]
    [InlineData("PipelineEntryRun")]
    public async Task PascalAliases_RouteUnderShellGroup(string alias)
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", alias, "--list", "--all"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == VerbForAlias(alias));
    }

    /// <summary>Verifies the bare non-group spellings are not commands: emitted invocations must carry the shell group.</summary>
    /// <param name="name">The bare command spelling.</param>
    [Theory]
    [InlineData("PipelinePreSetup")]
    [InlineData("PipelinePostSetup")]
    [InlineData("PipelineEntryRun")]
    [InlineData("pipeline-pre-setup")]
    [InlineData("pipeline-post-setup")]
    [InlineData("pipeline-entry-run")]
    public async Task BarePipelineSpellings_DoNotRoute(string name)
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync([name, "--list"]);

        Assert.Equal(2, exit);
        Assert.DoesNotContain(sink.Entries, entry => entry.Message == name);
    }

    /// <summary>Verifies <c>--all</c> listing includes the unlisted entry-run verb.</summary>
    [Fact]
    public async Task AllFlagListing_IncludesPipelineEntryRun()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Fetch", "--list", "--all"]);

        Assert.Equal(0, exit);
        Assert.Contains(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.PipelineEntryRun);
    }

    /// <summary>Verifies plain listing excludes the unlisted entry-run verb.</summary>
    [Fact]
    public async Task PlainListing_ExcludesPipelineEntryRun()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "Fetch", "--list"]);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(sink.Entries, entry => entry.Message == PlutoniumBuildVerbs.PipelineEntryRun);
    }

    private static string ResolveVerb(Type commandType)
    {
        var command = Activator.CreateInstance(commandType);
        var property = typeof(PlutoniumShellCommand).GetProperty(
            "Verb", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(command);
        Assert.NotNull(property);
        return Assert.IsType<string>(property.GetValue(command));
    }

    private static string[] InvocationTokens(string runScript, string composedLine)
    {
        Assert.StartsWith(runScript, composedLine, StringComparison.Ordinal);
        return composedLine[runScript.Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string VerbForAlias(string alias) => alias switch
    {
        "PipelinePreSetup" => PlutoniumBuildVerbs.PipelinePreSetup,
        "PipelinePostSetup" => PlutoniumBuildVerbs.PipelinePostSetup,
        "PipelineEntryRun" => PlutoniumBuildVerbs.PipelineEntryRun,
        _ => throw new ArgumentOutOfRangeException(nameof(alias), alias, "Unknown pipeline alias."),
    };
}
