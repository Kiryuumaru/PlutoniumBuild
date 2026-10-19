using Plutonium.Core.Logging;
using Plutonium.Core.Process;
using Xunit;

namespace Plutonium.Core.Tests;

/// <summary>
/// Terminal contract: captured output is masked through the masker injected into the
/// wrapper before it is returned or forwarded, non-zero exits surface through
/// <see cref="PlutoniumProcessResult.EnsureSucceeded"/>, timeouts throw, null
/// dependencies and requests are rejected, and request environment variables
/// (overriding same-named inherited values) and working directory reach the child process.
/// </summary>
[Collection("PipelineEnvironment")]
public sealed class PlutoniumTerminalTests
{
    private const string SecretVariableName = "PLUTONIUM_TERMINAL_TEST_SECRET";
    private const string MergedVariableName = "PLUTONIUM_TERMINAL_TEST_MERGED";
    private const string InheritedVariableName = "PLUTONIUM_TERMINAL_TEST_INHERITED";

    /// <summary>
    /// Verifies captured standard output and standard error plus the forwarded log lines are
    /// masked by the masker injected into the terminal while the logger has no secret
    /// registered, so a wrapper that omitted the masker would leak the raw secret.
    /// </summary>
    [Fact]
    public async Task RunAsync_MasksCapturedOutputWithOnlyTheInjectedMasker()
    {
        var secret = "terminal-secret-" + Guid.NewGuid().ToString("N");
        var sink = new RecordingSink();
        var logger = new PlutoniumLogger([sink]);
        var masker = new PlutoniumSecretMasker();
        masker.Register(secret);
        var terminal = new PlutoniumTerminal(new PlutoniumProcessRunner(), masker, logger);
        var script = OperatingSystem.IsWindows()
            ? $"echo %{SecretVariableName}% & echo %{SecretVariableName}% 1>&2"
            : $"echo \"${SecretVariableName}\"; echo \"${SecretVariableName}\" 1>&2";
        var request = CreateShellRequest(
            script,
            environmentVariables: new Dictionary<string, string>(StringComparer.Ordinal) { [SecretVariableName] = secret });

        var result = await terminal.RunAsync(request);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("***", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("***", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.StandardError, StringComparison.Ordinal);
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Information
                && entry.Message.Contains("***", StringComparison.Ordinal)
                && !entry.Message.Contains(secret, StringComparison.Ordinal));
        Assert.Contains(
            sink.Entries,
            entry => entry.Level == PlutoniumLogLevel.Warning
                && entry.Message.Contains("***", StringComparison.Ordinal)
                && !entry.Message.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    /// <summary>Verifies a non-zero exit is carried by the result and named by EnsureSucceeded.</summary>
    [Fact]
    public async Task RunAsync_NonZeroExitSurfacesThroughEnsureSucceeded()
    {
        var terminal = new PlutoniumTerminal(new PlutoniumProcessRunner(), new PlutoniumSecretMasker(), new PlutoniumLogger([new RecordingSink()]));
        var request = OperatingSystem.IsWindows()
            ? new PlutoniumProcessRequest("cmd", "/c exit 7")
            : new PlutoniumProcessRequest("/bin/sh", "-c \"exit 7\"");

        var result = await terminal.RunAsync(request);

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.Succeeded);
        var failure = Assert.Throws<InvalidOperationException>(result.EnsureSucceeded);
        Assert.Contains("exit code 7", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies a request timeout throws instead of waiting for the child to exit.</summary>
    [Fact]
    public async Task RunAsync_TimeoutThrowsTimeoutException()
    {
        var terminal = new PlutoniumTerminal(new PlutoniumProcessRunner(), new PlutoniumSecretMasker(), new PlutoniumLogger([new RecordingSink()]));
        var script = OperatingSystem.IsWindows() ? "ping -n 6 127.0.0.1 > nul" : "sleep 5";
        var request = CreateShellRequest(script, timeout: TimeSpan.FromMilliseconds(250));

        var failure = await Assert.ThrowsAsync<TimeoutException>(
            async () => await terminal.RunAsync(request));

        Assert.Contains("timed out after", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies constructing the terminal rejects a null process runner.</summary>
    [Fact]
    public void Constructor_RejectsNullRunner()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new PlutoniumTerminal(null!, new PlutoniumSecretMasker(), new PlutoniumLogger([new RecordingSink()]));
        });
    }

    /// <summary>Verifies constructing the terminal rejects a null secret masker.</summary>
    [Fact]
    public void Constructor_RejectsNullMasker()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new PlutoniumTerminal(new PlutoniumProcessRunner(), null!, new PlutoniumLogger([new RecordingSink()]));
        });
    }

    /// <summary>Verifies constructing the terminal rejects a null logger.</summary>
    [Fact]
    public void Constructor_RejectsNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new PlutoniumTerminal(new PlutoniumProcessRunner(), new PlutoniumSecretMasker(), null!);
        });
    }

    /// <summary>Verifies a null process request is rejected before any process starts.</summary>
    [Fact]
    public async Task RunAsync_RejectsNullRequest()
    {
        var terminal = new PlutoniumTerminal(new PlutoniumProcessRunner(), new PlutoniumSecretMasker(), new PlutoniumLogger([new RecordingSink()]));

        await Assert.ThrowsAsync<ArgumentNullException>(() => terminal.RunAsync(null!));
    }

    /// <summary>
    /// Verifies request environment variables reach the child while an inherited environment
    /// variable survives, so the request environment merges over the current one.
    /// </summary>
    [Fact]
    public async Task RunAsync_MergesRequestEnvironmentOverInheritedEnvironment()
    {
        var terminal = new PlutoniumTerminal(new PlutoniumProcessRunner(), new PlutoniumSecretMasker(), new PlutoniumLogger([new RecordingSink()]));
        var inheritedValue = "inherited-" + Guid.NewGuid().ToString("N");
        var mergedValue = "merged-" + Guid.NewGuid().ToString("N");
        var priorInherited = Environment.GetEnvironmentVariable(InheritedVariableName);
        var script = OperatingSystem.IsWindows()
            ? $"echo %{InheritedVariableName}% & echo %{MergedVariableName}%"
            : $"echo \"${InheritedVariableName}\"; echo \"${MergedVariableName}\"";
        var request = CreateShellRequest(
            script,
            environmentVariables: new Dictionary<string, string>(StringComparer.Ordinal) { [MergedVariableName] = mergedValue });
        try
        {
            Environment.SetEnvironmentVariable(InheritedVariableName, inheritedValue);

            var result = await terminal.RunAsync(request);

            Assert.Contains(inheritedValue, result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains(mergedValue, result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(InheritedVariableName, priorInherited);
        }
    }

    /// <summary>
    /// Verifies a request environment variable sharing a name with an inherited variable
    /// overrides it in the child process, so request values take precedence over inherited ones.
    /// </summary>
    [Fact]
    public async Task RunAsync_RequestEnvironmentOverridesInheritedVariable()
    {
        var terminal = new PlutoniumTerminal(new PlutoniumProcessRunner(), new PlutoniumSecretMasker(), new PlutoniumLogger([new RecordingSink()]));
        var inheritedValue = "inherited-" + Guid.NewGuid().ToString("N");
        var requestValue = "request-" + Guid.NewGuid().ToString("N");
        var priorInherited = Environment.GetEnvironmentVariable(InheritedVariableName);
        var script = OperatingSystem.IsWindows()
            ? $"echo %{InheritedVariableName}%"
            : $"echo \"${InheritedVariableName}\"";
        var request = CreateShellRequest(
            script,
            environmentVariables: new Dictionary<string, string>(StringComparer.Ordinal) { [InheritedVariableName] = requestValue });
        try
        {
            Environment.SetEnvironmentVariable(InheritedVariableName, inheritedValue);

            var result = await terminal.RunAsync(request);

            Assert.Contains(requestValue, result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(inheritedValue, result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(InheritedVariableName, priorInherited);
        }
    }

    /// <summary>Verifies the child process runs inside the requested working directory.</summary>
    [Fact]
    public async Task RunAsync_UsesRequestedWorkingDirectory()
    {
        var terminal = new PlutoniumTerminal(new PlutoniumProcessRunner(), new PlutoniumSecretMasker(), new PlutoniumLogger([new RecordingSink()]));
        var workingDirectory = Path.Combine(Path.GetTempPath(), "plutonium terminal cwd " + Guid.NewGuid().ToString("N"));
        var markerName = "cwd-" + Guid.NewGuid().ToString("N") + ".txt";
        Directory.CreateDirectory(workingDirectory);
        var script = OperatingSystem.IsWindows()
            ? $"echo relative> {markerName}"
            : $"echo relative > {markerName}";
        try
        {
            var result = await terminal.RunAsync(CreateShellRequest(script, workingDirectory: workingDirectory));

            Assert.Equal(0, result.ExitCode);
            Assert.True(
                File.Exists(Path.Combine(workingDirectory, markerName)),
                "The child process did not create the marker inside the requested working directory.");
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
            var strayMarker = Path.Combine(Environment.CurrentDirectory, markerName);
            if (File.Exists(strayMarker))
            {
                File.Delete(strayMarker);
            }
        }
    }

    private static PlutoniumProcessRequest CreateShellRequest(
        string script,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null) =>
        OperatingSystem.IsWindows()
            ? new PlutoniumProcessRequest("cmd", "/c " + script, workingDirectory, environmentVariables, timeout)
            : new PlutoniumProcessRequest(
                "/bin/sh",
                "-c \"" + script.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
                workingDirectory,
                environmentVariables,
                timeout);

    private sealed class RecordingSink : IPlutoniumLogSink
    {
        public List<(PlutoniumLogLevel Level, string Message)> Entries { get; } = [];

        public void Write(PlutoniumLogLevel level, string? scope, string message) =>
            Entries.Add((level, message));
    }
}
