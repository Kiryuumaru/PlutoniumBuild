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
/// Thin <c>git</c> surface actually invoked by Helpers:
/// current branch, remote tags, show, tag, and push.
/// Arguments travel as strings; no process-start details leak to callers.
/// </summary>
public sealed class PlutoniumGit
{
    private readonly PlutoniumProcessRunner _runner;
    private readonly PlutoniumSecretMasker? _masker;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumGit"/> class.
    /// </summary>
    /// <param name="runner">The process runner. Must not be null.</param>
    /// <param name="masker">Optional secret masker. May be null.</param>
    public PlutoniumGit(PlutoniumProcessRunner runner, PlutoniumSecretMasker? masker = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _masker = masker;
    }

    /// <summary>
    /// Returns the current branch via <c>git rev-parse --abbrev-ref HEAD</c>.
    /// </summary>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trimmed branch name.</returns>
    public async Task<string> GetCurrentBranchAsync(
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync("rev-parse --abbrev-ref HEAD", workingDirectory, cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
        return result.StandardOutput.Trim();
    }

    /// <summary>
    /// Lists remote tags via <c>git ls-remote --tags</c>.
    /// </summary>
    /// <param name="remoteUrl">The remote URL. Must not be null or whitespace and must not contain quotes or newlines.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result carrying raw <c>ls-remote</c> output. Never null.</returns>
    public Task<PlutoniumProcessResult> ListRemoteTagsAsync(
        string remoteUrl,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);
        ThrowIfUnquotable(remoteUrl, nameof(remoteUrl));
        return InvokeAsync($"ls-remote --tags \"{remoteUrl.Trim()}\"", workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Shows a revision path via <c>git show</c>.
    /// </summary>
    /// <param name="revision">The revision. Must not be null or whitespace and must not contain quotes or newlines.</param>
    /// <param name="path">The path within the revision. Must not be null or whitespace and must not contain quotes or newlines.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> ShowAsync(
        string revision,
        string path,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfUnquotable(revision, nameof(revision));
        ThrowIfUnquotable(path, nameof(path));
        return InvokeAsync($"show \"{revision.Trim()}:{path.Trim()}\"", workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Creates a tag via <c>git tag</c>.
    /// </summary>
    /// <param name="tag">The tag name. Must not be null or whitespace and must not contain quotes or newlines.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> CreateTagAsync(
        string tag,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ThrowIfUnquotable(tag, nameof(tag));
        return InvokeAsync($"tag \"{tag.Trim()}\"", workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Pushes a refspec via <c>git push</c>.
    /// </summary>
    /// <param name="remote">The remote name. Must not be null or whitespace and must not contain quotes or newlines.</param>
    /// <param name="refspec">The refspec. Must not be null or whitespace and must not contain quotes or newlines.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process result. Never null.</returns>
    public Task<PlutoniumProcessResult> PushAsync(
        string remote,
        string refspec,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(refspec);
        ThrowIfUnquotable(remote, nameof(remote));
        ThrowIfUnquotable(refspec, nameof(refspec));
        return InvokeAsync($"push \"{remote.Trim()}\" \"{refspec.Trim()}\"", workingDirectory, cancellationToken);
    }

    private Task<PlutoniumProcessResult> InvokeAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken) =>
        _runner.RunAsync(
            new PlutoniumProcessRequest("git", arguments, workingDirectory),
            _masker,
            cancellationToken);

    private static void ThrowIfUnquotable(string value, string paramName)
    {
        if (value.Contains('"', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Value must not contain quotes or newlines.", paramName);
        }
    }
}
