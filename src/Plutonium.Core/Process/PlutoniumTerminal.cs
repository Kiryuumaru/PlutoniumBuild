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
/// First-class terminal surface for entry code: runs arbitrary external tools
/// through <see cref="PlutoniumProcessRunner"/> with the secret masker applied to
/// captured output, then forwards non-empty standard output at Information and
/// non-empty standard error at Warning.
/// </summary>
public sealed class PlutoniumTerminal
{
    private readonly PlutoniumProcessRunner _runner;
    private readonly PlutoniumSecretMasker _masker;
    private readonly IPlutoniumLogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumTerminal"/> class.
    /// </summary>
    /// <param name="runner">The process runner. Must not be null.</param>
    /// <param name="masker">The secret masker applied to captured output. Must not be null.</param>
    /// <param name="logger">The logger receiving captured output. Must not be null.</param>
    public PlutoniumTerminal(
        PlutoniumProcessRunner runner,
        PlutoniumSecretMasker masker,
        IPlutoniumLogger logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(masker);
        ArgumentNullException.ThrowIfNull(logger);
        _runner = runner;
        _masker = masker;
        _logger = logger;
    }

    /// <summary>
    /// Runs the requested executable and forwards its captured output to the logger.
    /// </summary>
    /// <remarks>
    /// There is no shell: callers run <c>sh -c</c> or <c>cmd /c</c> themselves.
    /// <see cref="PlutoniumProcessRequest.Arguments"/> is one raw string and OS quoting is the caller's duty.
    /// Environment variables merge over the inherited environment.
    /// A null <see cref="PlutoniumProcessRequest.WorkingDirectory"/> inherits the current process directory.
    /// A timeout kills the process tree and throws <see cref="TimeoutException"/>.
    /// Standard output and error are captured after completion, never streamed. Standard input is not redirected or managed; interactive tools are unsupported and may read the inherited console or block.
    /// Non-zero exit codes do not throw here; call <see cref="PlutoniumProcessResult.EnsureSucceeded"/> to surface a failure.
    /// </remarks>
    /// <param name="request">The process request. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completed process result carrying masked output. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the process cannot start.</exception>
    /// <exception cref="TimeoutException">Thrown when <see cref="PlutoniumProcessRequest.Timeout"/> elapses; the process tree is killed first.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<PlutoniumProcessResult> RunAsync(
        PlutoniumProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _runner.RunAsync(request, _masker, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(result.StandardOutput))
        {
            _logger.Log(PlutoniumLogLevel.Information, result.StandardOutput);
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            _logger.Log(PlutoniumLogLevel.Warning, result.StandardError);
        }

        return result;
    }
}
