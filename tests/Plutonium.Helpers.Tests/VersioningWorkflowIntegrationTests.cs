using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;
using Plutonium.Helpers.Entries;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Tests;

/// <summary>
/// End-to-end versioning workflow against a hermetic local origin: workflow
/// generation writes and reconciles <c>versions.json</c>, Bump pushes HEAD, each
/// app's bump tag, and the force-moved branch trigger tag independently per app,
/// the watch exits with a failure once a bumped run reports failure, and
/// StatusWatch reconciles ticks until its token is canceled.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class VersioningWorkflowIntegrationTests
{
    /// <summary>
    /// Verifies workflow generation writes the reconciled latest, then keeps an
    /// ahead <c>versions.json</c> version when regenerated.
    /// </summary>
    [Fact]
    public async Task GitHubWorkflowGeneration_WritesAndReconcilesVersionsFile()
    {
        using var sandbox = GitSandbox.Create();
        sandbox.Tag("app-a/1.0.0", "app-a/1.0.0-passed");
        sandbox.Run(sandbox.Repository, "push", "-q", "origin", "--tags");
        sandbox.Commit("next");
        var sink = new RecordingSink();
        var context = CreateContext(new JsonVersionedBuild(), sandbox, sink);

        await PlutoniumBuildVerbs.RunGitHubWorkflowAsync(context);

        var versionsPath = Path.Combine(sandbox.Repository, "versions.json");
        Assert.True(File.Exists(versionsPath), "Expected versions.json at the repository root.");
        Assert.Equal("1.0.0", ReadFileVersion(versionsPath, "app-a", "main"));

        File.WriteAllText(versionsPath, "{\"app-a\":{\"main\":\"1.1.0\"}}");
        await PlutoniumBuildVerbs.RunGitHubWorkflowAsync(context);

        Assert.Equal("1.1.0", ReadFileVersion(versionsPath, "app-a", "main"));
    }

    /// <summary>
    /// Verifies Bump pushes HEAD, each app's independently computed bump tag, and
    /// force-moves the branch trigger tag to HEAD.
    /// </summary>
    [Fact]
    public async Task Bump_PushesHeadEveryAppBumpTagAndMovedBranchTrigger()
    {
        using var sandbox = GitSandbox.Create();
        sandbox.Tag(
            "app-a/1.0.0",
            "app-a/1.0.0-passed",
            "app-a/latest",
            "app-b/2.3.4",
            "app-b/2.3.4-passed",
            "app-b/latest",
            "bump-main");
        sandbox.Run(sandbox.Repository, "push", "-q", "origin", "--tags");
        var previousCommit = sandbox.Head;
        sandbox.Commit("next");
        var head = sandbox.Head;
        var sink = new RecordingSink();
        var context = CreateContext(new TwoAppBuild(), sandbox, sink);
        context.SplitArgs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["app-a"] = "patch",
            ["app-b"] = "patch",
        };

        await PlutoniumBuildVerbs.RunBumpAsync(context, watch: false);

        Assert.Equal(head, sandbox.RemoteRev("refs/heads/main"));
        Assert.Contains("app-a/1.0.1-bump", sandbox.RemoteTags());
        Assert.Contains("app-b/2.3.5-bump", sandbox.RemoteTags());
        Assert.Equal(head, sandbox.RemoteRev("refs/tags/app-a/1.0.1-bump"));
        Assert.Equal(head, sandbox.RemoteRev("refs/tags/app-b/2.3.5-bump"));
        Assert.Equal(head, sandbox.RemoteRev("refs/tags/bump-main"));
        Assert.NotEqual(previousCommit, sandbox.RemoteRev("refs/tags/bump-main"));
    }

    /// <summary>
    /// Verifies the post-bump watch reconciles until the bumped version reports a
    /// failed run, then throws the pipeline failure.
    /// </summary>
    [Fact]
    public async Task BumpWatch_ThrowsWhenBumpedVersionRunFails()
    {
        using var sandbox = GitSandbox.Create();
        sandbox.Tag("app-a/1.0.0", "app-a/1.0.0-passed");
        sandbox.Run(sandbox.Repository, "push", "-q", "origin", "--tags");
        sandbox.Commit("next");
        var sink = new RecordingSink();
        var context = CreateContext(new SingleAppBuild(), sandbox, sink);
        context.SplitArgs = new Dictionary<string, string?>(StringComparer.Ordinal) { ["app-a"] = "patch" };
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var simulator = new CancellationTokenSource();

        var simulateFailure = Task.Run(
            async () =>
            {
                while (!simulator.IsCancellationRequested)
                {
                    if (sandbox.RemoteTagExists("app-a/1.0.1-bump"))
                    {
                        sandbox.TagAndPush("app-a/1.0.1-failed");
                        return;
                    }

                    await Task.Delay(50, simulator.Token);
                }
            },
            simulator.Token);

        try
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlutoniumBuildVerbs.RunBumpAsync(context, watch: true, watchdog.Token));

            Assert.Equal("Pipeline run has failed.", failure.Message);
        }
        finally
        {
            simulator.Cancel();
            try
            {
                await simulateFailure;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>
    /// Verifies StatusWatch keeps reconciling after the first tick and stops on cancellation.
    /// </summary>
    [Fact]
    public async Task StatusWatch_ReconcilesUntilCanceled()
    {
        using var sandbox = GitSandbox.Create();
        sandbox.Tag("app-a/1.0.0", "app-a/1.0.0-passed");
        sandbox.Run(sandbox.Repository, "push", "-q", "origin", "--tags");
        var sink = new RecordingSink();
        var context = CreateContext(new SingleAppBuild(), sandbox, sink);
        using var cancellation = new CancellationTokenSource();

        var watch = PlutoniumBuildVerbs.RunStatusWatchAsync(context, cancellation.Token);
        await WaitUntilAsync(
            () => sink.Entries.Any(entry => entry.Message.Contains("watch tick 1:", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
        Assert.Contains(
            sink.Entries,
            entry => entry.Message == "Watching branch 'main'.");
    }

    private static string ReadFileVersion(string path, string appId, string environment)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(appId).GetProperty(environment).GetString()!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException("The watch did not reach the expected tick in time.");
            }

            await Task.Delay(25);
        }
    }

    private static PlutoniumPipelineContext CreateContext(object build, GitSandbox sandbox, RecordingSink sink)
    {
        var logger = new PlutoniumLogger([sink]);
        var services = PlutoniumBuildServices.Create(
            new PlutoniumProcessRunner(logger), logger, new PlutoniumSecretMasker(), new PlutoniumHost(logger));
        var options = new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath(sandbox.Repository),
            new PlutoniumPath(Path.Combine(sandbox.Root, "temp")),
            new PlutoniumPath(Path.Combine(sandbox.Root, "out")),
            new PlutoniumPath(Path.Combine(sandbox.Root, "cache")),
            sandbox.Origin);
        return new PlutoniumPipelineContext(build, options, services, PlutoniumPipelineType.Local);
    }

    private sealed class JsonVersionedBuild
    {
        public WorkflowConfigEntry WorkflowConfig => definition => definition.UseJsonFileVersioning(true);

        public BuildEntry AppA => definition => definition.AppId("app-a");
    }

    private sealed class TwoAppBuild
    {
        public WorkflowConfigEntry WorkflowConfig => definition => definition;

        public BuildEntry AppA => definition => definition.AppId("app-a");

        public BuildEntry AppB => definition => definition.AppId("app-b");
    }

    private sealed class SingleAppBuild
    {
        public WorkflowConfigEntry WorkflowConfig => definition => definition;

        public BuildEntry AppA => definition => definition.AppId("app-a");
    }

    private sealed class RecordingSink : IPlutoniumLogSink
    {
        public List<(PlutoniumLogLevel Level, string Message)> Entries { get; } = [];

        public void Write(PlutoniumLogLevel level, string? scope, string message) =>
            Entries.Add((level, message));
    }
}
