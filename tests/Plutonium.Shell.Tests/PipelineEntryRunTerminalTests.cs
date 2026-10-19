using ApplicationBuilderHelpers;
using Plutonium.Core.Logging;
using Plutonium.Core.Process;
using Plutonium.Core.Workflows;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Runtime;
using Xunit;

namespace Plutonium.Shell.Tests;

/// <summary>
/// Entry-run terminal behavior through the real CLI: target-kind and dependent-kind
/// entries reach <see cref="PlutoniumTerminal"/> through the run context, run a real
/// external tool, forward the captured output to the logger, and surface non-zero
/// exits and timeouts as a failed command with diagnostic evidence. The marker path
/// travels through an environment variable so paths with spaces survive both shells.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class PipelineEntryRunTerminalTests : IDisposable
{
    private const string MarkerVariableName = "PLUTONIUM_TERMINAL_MARKER";
    private readonly string? _entryVariableBackup;
    private readonly string? _payloadVariableBackup;
    private readonly string? _markerVariableBackup;

    /// <summary>Backs up and clears the pipeline environment variables used by the tests.</summary>
    public PipelineEntryRunTerminalTests()
    {
        _entryVariableBackup = Environment.GetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName);
        _payloadVariableBackup = Environment.GetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName);
        _markerVariableBackup = Environment.GetEnvironmentVariable(MarkerVariableName);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, null);
        Environment.SetEnvironmentVariable(MarkerVariableName, null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, _entryVariableBackup);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, _payloadVariableBackup);
        Environment.SetEnvironmentVariable(MarkerVariableName, _markerVariableBackup);
    }

    /// <summary>
    /// Verifies a target-kind entry runs a terminal command end-to-end: the marker file lands
    /// at the env-carried path with its content, and captured standard output is forwarded to
    /// the log sink at Information.
    /// </summary>
    [Fact]
    public async Task TargetEntry_RunsTerminalCommandAndForwardsOutput()
    {
        var directory = CreateMarkerDirectory();
        var markerPath = Path.Combine(directory, "terminal marker.txt");
        try
        {
            Environment.SetEnvironmentVariable(MarkerVariableName, markerPath);
            Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "TerminalTarget");
            Environment.SetEnvironmentVariable(
                PlutoniumPipelineContract.PayloadVariableName,
                CreatePayload("TerminalTarget").ToPayload());
            var (builder, sink) = CreateShell();

            var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(markerPath), "The terminal command did not create the marker file.");
            Assert.Equal("terminal-ran", File.ReadAllText(markerPath).Trim());
            Assert.Contains(
                sink.Entries,
                entry => entry.Level == PlutoniumLogLevel.Information
                    && entry.Message.Contains("terminal-output", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a dependent-kind entry reaches the terminal through its own run-context
    /// setup path and runs the same end-to-end command with the marker and forwarded output.
    /// </summary>
    [Fact]
    public async Task DependentEntry_RunsTerminalCommandAndForwardsOutput()
    {
        var directory = CreateMarkerDirectory();
        var markerPath = Path.Combine(directory, "terminal marker.txt");
        try
        {
            Environment.SetEnvironmentVariable(MarkerVariableName, markerPath);
            Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "TerminalDependent");
            Environment.SetEnvironmentVariable(
                PlutoniumPipelineContract.PayloadVariableName,
                CreatePayload("TerminalDependent").ToPayload());
            var (builder, sink) = CreateShell();

            var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(markerPath), "The terminal command did not create the marker file.");
            Assert.Equal("terminal-ran", File.ReadAllText(markerPath).Trim());
            Assert.Contains(
                sink.Entries,
                entry => entry.Level == PlutoniumLogLevel.Information
                    && entry.Message.Contains("terminal-output", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a non-zero child exit fails the CLI command with exit 1 and an Error
    /// line naming the child exit code.
    /// </summary>
    [Fact]
    public async Task NonZeroExit_FailsCommandWithExitCodeDiagnostic()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "TerminalFailure");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.PayloadVariableName,
            CreatePayload("TerminalFailure").ToPayload());
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

        Assert.Equal(1, exit);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("exit code 9", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a terminal timeout fails the CLI command with exit 1 and an Error
    /// line carrying the timeout diagnostic.
    /// </summary>
    [Fact]
    public async Task Timeout_FailsCommandWithTimeoutDiagnostic()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "TerminalTimeout");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.PayloadVariableName,
            CreatePayload("TerminalTimeout").ToPayload());
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

        Assert.Equal(1, exit);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("timed out after", StringComparison.Ordinal));
    }

    private static (ApplicationBuilder Builder, CapturingSink Sink) CreateShell()
    {
        var sink = new CapturingSink();
        var logger = new PlutoniumLogger([sink]);
        var builder = ApplicationBuilder.Create();
        builder.AddPlutoniumShell(new TerminalEntryBuild(), ShellTestFixtures.CreateOptions(), logger);
        return (builder, sink);
    }

    private static string CreateMarkerDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "plutonium terminal " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static PlutoniumPipelinePreSetup CreatePayload(string entryId)
    {
        var payload = new PlutoniumPipelinePreSetup
        {
            Branch = "main",
            Environment = "main",
            BuildId = "20240101120000.abc1234",
        };
        payload.EntrySetupMap[entryId] = new PlutoniumEntrySetup
        {
            Id = entryId,
            Condition = true,
            RunTypes = { ["shelltest"] = PlutoniumRunType.Commit },
        };
        payload.AppRunEntryMap["shelltest"] = new PlutoniumAppRunEntry
        {
            AppId = "shelltest",
            Environment = "main",
            Version = "0.1.0",
        };
        return payload;
    }

    private static PlutoniumProcessRequest CreateShellRequest(
        string script,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null) =>
        OperatingSystem.IsWindows()
            ? new PlutoniumProcessRequest("cmd", "/c " + script, null, environmentVariables, timeout)
            : new PlutoniumProcessRequest(
                "/bin/sh",
                "-c \"" + script.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
                null,
                environmentVariables,
                timeout);

    private sealed class TerminalEntryBuild
    {
        public WorkflowConfigEntry WorkflowConfig => definition => definition;

        public BuildEntry TerminalTarget => definition => definition
            .AppId("shelltest")
            .Execute(RunMarkerCommandAsync);

        public TestEntry TerminalDependent => definition => definition
            .AppId("shelltest")
            .Execute(RunMarkerCommandAsync);

        public BuildEntry TerminalFailure => definition => definition
            .AppId("shelltest")
            .Execute(RunFailureCommandAsync);

        public TestEntry TerminalTimeout => definition => definition
            .AppId("shelltest")
            .Execute(RunTimeoutCommandAsync);

        private static async Task RunMarkerCommandAsync(IRunContext context)
        {
            var markerPath = Environment.GetEnvironmentVariable(MarkerVariableName)
                ?? throw new InvalidOperationException("The terminal marker variable is not set.");
            var script = OperatingSystem.IsWindows()
                ? "echo terminal-ran> \"%" + MarkerVariableName + "%\" & echo terminal-output"
                : "echo terminal-ran > \"$" + MarkerVariableName + "\"; echo terminal-output";
            var request = CreateShellRequest(
                script,
                new Dictionary<string, string>(StringComparer.Ordinal) { [MarkerVariableName] = markerPath });
            var result = await context.Terminal.RunAsync(request).ConfigureAwait(false);
            result.EnsureSucceeded();
        }

        private static async Task RunFailureCommandAsync(IRunContext context)
        {
            var result = await context.Terminal.RunAsync(CreateShellRequest("exit 9")).ConfigureAwait(false);
            result.EnsureSucceeded();
        }

        private static async Task RunTimeoutCommandAsync(IRunContext context)
        {
            var script = OperatingSystem.IsWindows() ? "ping -n 6 127.0.0.1 > nul" : "sleep 5";
            await context.Terminal.RunAsync(
                CreateShellRequest(script, timeout: TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);
        }
    }
}
