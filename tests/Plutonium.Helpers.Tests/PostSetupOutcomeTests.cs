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
/// Post-setup outcome contract: every discovered run entry's normalized result is
/// consumed, any <c>error</c> aborts before success stamping, success writes the
/// version marker, and finalization always runs before a failure is thrown.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class PostSetupOutcomeTests : IDisposable
{
    private readonly Dictionary<string, string?> _backup = new(StringComparer.Ordinal);
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "plutonium-post-setup-" + Guid.NewGuid().ToString("N"));

    /// <summary>Clears the plain run-result variables and backs up prior values.</summary>
    public PostSetupOutcomeTests()
    {
        foreach (var entryId in EntryIds)
        {
            var name = PlutoniumPipelineContract.RunResultVariableName(entryId);
            _backup[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var pair in _backup)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IReadOnlyList<string> EntryIds => ["my-job", "my-publish"];

    /// <summary>Verifies an error on any entry aborts post-setup and writes no success marker.</summary>
    /// <param name="errorEntry">The entry reporting the error.</param>
    [Theory]
    [InlineData("my-job")]
    [InlineData("my-publish")]
    public async Task PostSetup_FailsWhenAnyEntryResultIsError(string errorEntry)
    {
        var sink = new RecordingSink();
        var (context, run) = CreateRun(sink, hasRelease: true);
        foreach (var entryId in EntryIds)
        {
            Environment.SetEnvironmentVariable(
                PlutoniumPipelineContract.RunResultVariableName(entryId),
                entryId == errorEntry ? "error" : "success");
        }

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumPreSetup.RunPostSetupAsync(context, run));

        Assert.Equal("Run has error(s).", failure.Message);
        Assert.False(File.Exists(MarkerPath(context)));
        Assert.Contains(
            sink.Entries,
            entry => entry.Message == $"Entry '{errorEntry}' result: error");
        Assert.Contains(
            sink.Entries,
            entry => entry.Message == $"Entry '{OtherEntry(errorEntry)}' result: success");
    }

    /// <summary>Verifies a non-error result set writes the success marker with the released version.</summary>
    [Fact]
    public async Task PostSetup_StampsSuccessWhenResultsAreNotErrors()
    {
        var sink = new RecordingSink();
        var (context, run) = CreateRun(sink, hasRelease: true);
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.RunResultVariableName("my-job"), "success");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.RunResultVariableName("my-publish"), "skipped");

        await PlutoniumPreSetup.RunPostSetupAsync(context, run);

        Assert.True(File.Exists(MarkerPath(context)), "Expected the success marker file.");
        Assert.Equal("1.2.3", File.ReadAllText(MarkerPath(context)));
        Assert.Contains(
            sink.Entries,
            entry => entry.Message == "Release my-app/1.2.3: 1.2.3");
    }

    /// <summary>Verifies a failing run with no release payload still throws after consuming results.</summary>
    [Fact]
    public async Task PostSetup_ThrowsOnErrorEvenWithoutReleasePayload()
    {
        var sink = new RecordingSink();
        var (context, run) = CreateRun(sink, hasRelease: false);
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.RunResultVariableName("my-job"), "error");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumPreSetup.RunPostSetupAsync(context, run));

        Assert.Equal("Run has error(s).", failure.Message);
        Assert.False(File.Exists(MarkerPath(context)));
    }

    /// <summary>Verifies post-setup finalization runs before the failure escapes.</summary>
    [Fact]
    public async Task PostSetup_FinalizesBeforeThrowing()
    {
        var sink = new RecordingSink();
        var pipeline = new RecordingPipeline();
        var (context, run) = CreateRun(sink, hasRelease: true, pipeline: pipeline);
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.RunResultVariableName("my-job"), "error");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlutoniumPreSetup.RunPostSetupAsync(context, run));

        Assert.Equal(1, pipeline.FinalizePostSetupCalls);
        Assert.Equal(1, pipeline.PreparePostSetupCalls);
    }

    /// <summary>A draft-release deletion failure must not suppress failure tags or the run error.</summary>
    [Fact]
    public async Task PostSetup_PushesFailureTagsWhenReleaseDeletionFails()
    {
        using var sandbox = GitSandbox.Create();
        var sink = new RecordingSink();
        var (context, run) = CreateRun(
            sink,
            hasRelease: true,
            pipelineType: PlutoniumPipelineType.GitHub,
            repositoryRoot: sandbox.Repository);
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.RunResultVariableName("my-job"), "error");

        var fakeBin = CreateFakeGh();
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                fakeBin + Path.PathSeparator + originalPath);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PlutoniumPreSetup.RunPostSetupAsync(context, run));

            Assert.Equal("Run has error(s).", failure.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }

        var tags = sandbox.RemoteTags();
        Assert.Contains("my-app/1.2.3-failed", tags);
        Assert.Contains("my-app/1.2.3", tags);
        Assert.DoesNotContain("my-app/1.2.3-passed", tags);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Warning &&
                     entry.Message.Contains("was not deleted", StringComparison.Ordinal));
    }

    private string CreateFakeGh()
    {
        var fakeBin = Path.Combine(_root, "fake-bin");
        Directory.CreateDirectory(fakeBin);
        var scriptPath = Path.Combine(fakeBin, "gh");
        File.WriteAllText(scriptPath, "#!/usr/bin/env bash\nexit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Windows resolves an extensionless command through gh.exe; an invalid image
        // exercises the process-start failure branch, which must be handled the same way.
        File.WriteAllText(Path.Combine(fakeBin, "gh.exe"), "not a valid executable");
        return fakeBin;
    }

    private static string OtherEntry(string entryId) =>
        entryId == "my-job" ? "my-publish" : "my-job";

    private static string MarkerPath(PlutoniumPipelineContext context) =>
        context.Options.TemporaryDirectory.Combine("my-app.release").Value;

    private (PlutoniumPipelineContext Context, PlutoniumPipelineRun Run) CreateRun(
        RecordingSink sink,
        bool hasRelease,
        IPlutoniumPipeline? pipeline = null,
        PlutoniumPipelineType pipelineType = PlutoniumPipelineType.Local,
        string? repositoryRoot = null)
    {
        var entries = CreateEntries();
        var logger = new PlutoniumLogger([sink]);
        var services = PlutoniumBuildServices.Create(
            new PlutoniumProcessRunner(logger), logger, new PlutoniumSecretMasker(), new PlutoniumHost(logger));
        var options = new PlutoniumBuildOptions(
            ["main"],
            "main",
            new PlutoniumPath(repositoryRoot ?? Path.Combine(_root, "repo")),
            new PlutoniumPath(Path.Combine(_root, "temp")),
            new PlutoniumPath(Path.Combine(_root, "out")),
            new PlutoniumPath(Path.Combine(_root, "cache")),
            "https://example.test/org/repo.git");
        var context = new PlutoniumPipelineContext(new object(), options, services, pipelineType)
        {
            Entries = entries,
            PreSetup = CreatePayload(hasRelease),
        };
        var run = new PlutoniumPipelineRun(
            pipeline ?? new PlutoniumLocalPipeline(),
            new PlutoniumPipelineInfo("main", PlutoniumTriggerType.Local),
            pipelineType);
        return (context, run);
    }

    private static PlutoniumPipelinePreSetup CreatePayload(bool hasRelease)
    {
        var payload = new PlutoniumPipelinePreSetup
        {
            Branch = "main",
            TriggerType = PlutoniumTriggerType.Local,
            Environment = "main",
            BuildId = "20240101120000.abc1234",
            HasRelease = hasRelease,
        };
        payload.AppRunEntryMap["my-app"] = new PlutoniumAppRunEntry
        {
            AppId = "my-app",
            Environment = "main",
            Version = "1.2.3",
            OldVersion = "1.2.2",
            HasRelease = hasRelease,
        };
        return payload;
    }

    private static PlutoniumAllEntries CreateEntries()
    {
        var buildEntry = new BuildEntryDefinition();
        buildEntry.Id = "my-job";
        buildEntry.AppIds = [new PlutoniumAppId("my-app")];
        var publishEntry = new PublishEntryDefinition();
        publishEntry.Id = "my-publish";
        publishEntry.AppIds = [new PlutoniumAppId("my-app")];
        return new PlutoniumAllEntries(
            new Dictionary<string, PlutoniumAppEntries>(StringComparer.Ordinal),
            new WorkflowConfigEntryDefinition(),
            new Dictionary<string, ITestEntryDefinition>(StringComparer.Ordinal),
            new Dictionary<string, IBuildEntryDefinition>(StringComparer.Ordinal) { ["my-job"] = buildEntry },
            new Dictionary<string, IPublishEntryDefinition>(StringComparer.Ordinal) { ["my-publish"] = publishEntry },
            new Dictionary<string, IRunEntryDefinition>(StringComparer.Ordinal)
            {
                ["my-job"] = buildEntry,
                ["my-publish"] = publishEntry,
            });
    }

    private sealed class RecordingPipeline : IPlutoniumPipeline
    {
        private readonly PlutoniumLocalPipeline _inner = new();

        public int PreparePostSetupCalls { get; private set; }

        public int FinalizePostSetupCalls { get; private set; }

        public PlutoniumPipelineType PipelineType => PlutoniumPipelineType.Local;

        public Task<PlutoniumPipelineInfo> GetPipelineInfoAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default) =>
            _inner.GetPipelineInfoAsync(context, cancellationToken);

        public Task<PlutoniumPipelinePreSetup> GetPipelinePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default) =>
            _inner.GetPipelinePreSetupAsync(context, cancellationToken);

        public Task PreparePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default) =>
            _inner.PreparePreSetupAsync(context, cancellationToken);

        public Task FinalizePreSetupAsync(PlutoniumPipelineContext context, PlutoniumPipelinePreSetup preSetup, CancellationToken cancellationToken = default) =>
            _inner.FinalizePreSetupAsync(context, preSetup, cancellationToken);

        public Task PrepareEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default) =>
            _inner.PrepareEntryRunAsync(context, entry, cancellationToken);

        public Task FinalizeEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default) =>
            _inner.FinalizeEntryRunAsync(context, entry, cancellationToken);

        public Task PreparePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
        {
            PreparePostSetupCalls++;
            return _inner.PreparePostSetupAsync(context, cancellationToken);
        }

        public Task FinalizePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
        {
            FinalizePostSetupCalls++;
            return _inner.FinalizePostSetupAsync(context, cancellationToken);
        }

        public Task<PlutoniumWorkflowBuildResult> BuildWorkflowAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default) =>
            _inner.BuildWorkflowAsync(context, cancellationToken);
    }

    private sealed class RecordingSink : IPlutoniumLogSink
    {
        public List<(PlutoniumLogLevel Level, string Message)> Entries { get; } = [];

        public void Write(PlutoniumLogLevel level, string? scope, string message) =>
            Entries.Add((level, message));
    }
}
