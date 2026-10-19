using ApplicationBuilderHelpers;
using Plutonium.Core.Logging;
using Plutonium.Core.Workflows;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Runtime;
using Xunit;

namespace Plutonium.Shell.Tests;

/// <summary>
/// Entry-run command behavior through the real CLI: the <c>PLUTONIUM_ENTRY</c>
/// selector and the <c>PLUTONIUM_PRE_SETUP</c> payload drive diagnostics and exit
/// codes, and a false entry condition skips with success.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class PipelineEntryRunCommandTests : IDisposable
{
    private readonly string? _entryVariableBackup;
    private readonly string? _payloadVariableBackup;

    /// <summary>Backs up and clears the pipeline environment variables used by the tests.</summary>
    public PipelineEntryRunCommandTests()
    {
        _entryVariableBackup = Environment.GetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName);
        _payloadVariableBackup = Environment.GetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, null);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, _entryVariableBackup);
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, _payloadVariableBackup);
    }

    /// <summary>Verifies a missing entry selector exits 1 and logs the regeneration diagnostic.</summary>
    [Fact]
    public async Task MissingEntrySelector_ExitsOneWithDiagnostic()
    {
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

        Assert.Equal(1, exit);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("PLUTONIUM_ENTRY is missing or empty", StringComparison.Ordinal));
    }

    /// <summary>Verifies a missing or malformed payload exits 1 and logs the regeneration diagnostic.</summary>
    /// <param name="payload">The raw payload environment value, or null for a missing value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    [InlineData("bm90IGpzb24=")]
    public async Task MissingOrMalformedPayload_ExitsOneWithDiagnostic(string? payload)
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "ShellTest");
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.PayloadVariableName, payload);
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

        Assert.Equal(1, exit);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("payload for entry 'ShellTest' is missing or malformed", StringComparison.Ordinal));
    }

    /// <summary>Verifies an undiscovered entry id exits 1 and logs the regeneration diagnostic.</summary>
    [Fact]
    public async Task UnknownEntryId_ExitsOneWithDiagnostic()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "NoSuchEntry");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.PayloadVariableName,
            new PlutoniumPipelinePreSetup().ToPayload());
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

        Assert.Equal(1, exit);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("Entry id 'NoSuchEntry' is not a discovered run entry", StringComparison.Ordinal));
    }

    /// <summary>Verifies a payload without a setup for the selected entry exits 1 and logs the regeneration diagnostic.</summary>
    [Fact]
    public async Task MissingEntrySetup_ExitsOneWithDiagnostic()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "ShellTest");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.PayloadVariableName,
            new PlutoniumPipelinePreSetup().ToPayload());
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

        Assert.Equal(1, exit);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Error
                && entry.Message.Contains("has no setup for entry 'ShellTest'", StringComparison.Ordinal));
    }

    /// <summary>Verifies a false condition skips the entry and exits 0.</summary>
    [Fact]
    public async Task FalseCondition_ExitsZeroAfterSkip()
    {
        Environment.SetEnvironmentVariable(PlutoniumPipelineContract.EntryVariableName, "ShellTest");
        Environment.SetEnvironmentVariable(
            PlutoniumPipelineContract.PayloadVariableName,
            CreatePayloadWithSetup().ToPayload());
        var (builder, sink) = CreateShell();

        var exit = await builder.RunAsync(["shell", "pipeline-entry-run"]);

        Assert.Equal(0, exit);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Information
                && entry.Message == "Running pipeline entry 'ShellTest'.");
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Information
                && entry.Message == "Entry 'ShellTest' skipped (condition false).");
    }

    private static (ApplicationBuilder Builder, CapturingSink Sink) CreateShell()
    {
        var sink = new CapturingSink();
        var logger = new PlutoniumLogger([sink]);
        var builder = ApplicationBuilder.Create();
        builder.AddPlutoniumShell(new EntryRunBuild(), ShellTestFixtures.CreateOptions(), logger);
        return (builder, sink);
    }

    private static PlutoniumPipelinePreSetup CreatePayloadWithSetup()
    {
        var payload = new PlutoniumPipelinePreSetup
        {
            Branch = "main",
            Environment = "main",
            BuildId = "20240101120000.abc1234",
        };
        payload.EntrySetupMap["ShellTest"] = new PlutoniumEntrySetup
        {
            Id = "ShellTest",
            Condition = false,
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

    private sealed class EntryRunBuild
    {
        public WorkflowConfigEntry WorkflowConfig => definition => definition;

        public TestEntry ShellTest => definition => definition.AppId("shelltest").Condition(false);
    }
}
