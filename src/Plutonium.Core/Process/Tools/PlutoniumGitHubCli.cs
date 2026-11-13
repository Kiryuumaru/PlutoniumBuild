using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Process.Tools;

/// <summary>
/// Thin <c>gh</c> release surface: create, upload, edit, and delete passthroughs.
/// Arguments travel as strings; no process-start details leak to callers.
/// </summary>
public sealed class PlutoniumGitHubCli
{
    private readonly PlutoniumProcessRunner _runner;
    private readonly PlutoniumSecretMasker? _masker;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumGitHubCli"/> class.
    /// </summary>
    /// <param name="runner">The process runner. Must not be null.</param>
    /// <param name="masker">Optional secret masker. May be null.</param>
    public PlutoniumGitHubCli(PlutoniumProcessRunner runner, PlutoniumSecretMasker? masker = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _masker = masker;
    }

    /// <summary>
    /// Runs <c>gh release create</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>release create</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> CreateReleaseAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunGhAsync("release create " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Runs <c>gh release upload</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>release upload</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> UploadAssetAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunGhAsync("release upload " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Runs <c>gh release edit</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>release edit</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> EditReleaseAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunGhAsync("release edit " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Runs <c>gh release delete</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>release delete</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> DeleteReleaseAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunGhAsync("release delete " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    private Task<PlutoniumProcessResult> RunGhAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken) =>
        _runner.RunAsync(
            new PlutoniumProcessRequest("gh", arguments, workingDirectory),
            _masker,
            cancellationToken);
}
