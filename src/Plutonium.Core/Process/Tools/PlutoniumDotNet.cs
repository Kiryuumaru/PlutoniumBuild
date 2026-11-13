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
/// Thin <c>dotnet</c> surface: build, test, pack, and run passthroughs over the shared runner.
/// Arguments travel as strings; no process-start details leak to callers.
/// </summary>
public sealed class PlutoniumDotNet
{
    private readonly PlutoniumProcessRunner _runner;
    private readonly PlutoniumSecretMasker? _masker;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumDotNet"/> class.
    /// </summary>
    /// <param name="runner">The process runner. Must not be null.</param>
    /// <param name="masker">Optional secret masker. May be null.</param>
    public PlutoniumDotNet(PlutoniumProcessRunner runner, PlutoniumSecretMasker? masker = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _masker = masker;
    }

    /// <summary>
    /// Runs <c>dotnet build</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>build</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> BuildAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunDotNetAsync("build " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Runs <c>dotnet test</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>test</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> TestAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunDotNetAsync("test " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Runs <c>dotnet pack</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>pack</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> PackAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunDotNetAsync("pack " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Runs <c>dotnet run</c> with the given arguments.
    /// </summary>
    /// <param name="arguments">Arguments after <c>run</c>. Must not be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunDotNetAsync("run " + arguments.TrimStart(), workingDirectory, cancellationToken);
    }

    private Task<PlutoniumProcessResult> RunDotNetAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken) =>
        _runner.RunAsync(
            new PlutoniumProcessRequest("dotnet", arguments, workingDirectory),
            _masker,
            cancellationToken);
}
