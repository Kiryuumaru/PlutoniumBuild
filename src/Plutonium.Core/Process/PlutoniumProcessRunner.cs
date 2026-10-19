using System.Diagnostics;
using System.Text;
using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Process;

/// <summary>
/// Frozen request describing one external process invocation.
/// Arguments travel as a single string; no process-start details leak to callers.
/// </summary>
public sealed class PlutoniumProcessRequest
{
    /// <summary>Gets the executable file name or path. Never null or whitespace.</summary>
    public string FileName { get; }

    /// <summary>Gets the argument string. Never null.</summary>
    public string Arguments { get; }

    /// <summary>Gets the working directory, or null to inherit the current one.</summary>
    public string? WorkingDirectory { get; }

    /// <summary>Gets additional environment variables. Never null.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    /// <summary>Gets the timeout, or null for no timeout.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumProcessRequest"/> class.
    /// </summary>
    /// <param name="fileName">The executable. Must not be null or whitespace.</param>
    /// <param name="arguments">The argument string. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit. When provided, must not be whitespace.</param>
    /// <param name="environmentVariables">Additional environment variables. Must not be null; keys must not be null or whitespace and values must not be null.</param>
    /// <param name="timeout">The timeout, or null for none. When provided, must be greater than zero.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workingDirectory"/> is whitespace or an environment key is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is zero or negative.</exception>
    public PlutoniumProcessRequest(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (workingDirectory is not null && string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Working directory must not be whitespace.", nameof(workingDirectory));
        }

        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero.");
        }

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key, nameof(environmentVariables));
                ArgumentNullException.ThrowIfNull(pair.Value, nameof(environmentVariables));
            }
        }

        FileName = fileName.Trim();
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        EnvironmentVariables = environmentVariables ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Timeout = timeout;
    }
}

/// <summary>
/// Runs external executables with capture, secret masking, timeout, and cancellation.
/// Returns a frozen <see cref="PlutoniumProcessResult"/>; never leaks process handles.
/// </summary>
public sealed class PlutoniumProcessRunner
{
    private readonly IPlutoniumLogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumProcessRunner"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for start/finish lines. May be null.</param>
    public PlutoniumProcessRunner(IPlutoniumLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs the requested executable, capturing stdout/stderr with secrets masked.
    /// </summary>
    /// <remarks>
    /// The start line is masked with <paramref name="masker"/> before logging.
    /// Callers register every secret value via
    /// <see cref="IPlutoniumLogger.RegisterSecret"/> so logger-side masking
    /// also applies should the command line reach a sink unmasked.
    /// </remarks>
    /// <param name="request">The process request. Must not be null.</param>
    /// <param name="masker">Optional secret masker applied to captured output. May be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    /// <exception cref="TimeoutException">Thrown when <see cref="PlutoniumProcessRequest.Timeout"/> elapses.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<PlutoniumProcessResult> RunAsync(
        PlutoniumProcessRequest request,
        PlutoniumSecretMasker? masker = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var commandLine = $"Running '{request.FileName} {request.Arguments}'.".TrimEnd();
        _logger?.Log(PlutoniumLogLevel.Debug, masker is null ? commandLine : masker.Mask(commandLine));

        using var process = new global::System.Diagnostics.Process();
        process.StartInfo.FileName = request.FileName;
        process.StartInfo.Arguments = request.Arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        if (request.WorkingDirectory is not null)
        {
            process.StartInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var pair in request.EnvironmentVariables)
        {
            process.StartInfo.Environment[pair.Key] = pair.Value;
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        using var timeoutSource = request.Timeout.HasValue
            ? new CancellationTokenSource(request.Timeout.Value)
            : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource?.Token ?? CancellationToken.None);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Process '{request.FileName}' failed to start.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Process '{request.FileName}' failed to start: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource?.IsCancellationRequested == true)
        {
            TryKill(process);
            throw new TimeoutException($"Process '{request.FileName}' timed out after {request.Timeout}.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var maskedOut = masker is null ? stdout.ToString() : masker.Mask(stdout.ToString());
        var maskedErr = masker is null ? stderr.ToString() : masker.Mask(stderr.ToString());
        var result = new PlutoniumProcessResult(process.ExitCode, maskedOut, maskedErr);

        _logger?.Log(PlutoniumLogLevel.Debug, $"Process '{request.FileName}' exited with code {result.ExitCode}.");
        return result;
    }

    private static void TryKill(global::System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
