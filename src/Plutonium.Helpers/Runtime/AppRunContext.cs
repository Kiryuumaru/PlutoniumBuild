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
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Runtime;
/// <summary>
/// Per-app run context: version payloads plus the per-app output directory.
/// Only one of <see cref="BumpVersion"/> / <see cref="PullRequestVersion"/> is
/// non-null per run; both null means a plain commit or local run.
/// <see cref="IsBump"/> / <see cref="IsPullRequest"/> are authoritative via payload
/// non-null (payload wins); <see cref="RunType"/> flags are advisory for
/// Local/Commit/Target.
/// </summary>
public sealed class AppRunContext
{
    /// <summary>Gets the application identifier. Never null.</summary>
    public PlutoniumAppId AppId { get; }

    /// <summary>Gets the run type flags.</summary>
    public PlutoniumRunType RunType { get; }

    /// <summary>Gets the application version. Never null.</summary>
    public PlutoniumAppVersion AppVersion { get; }

    /// <summary>Gets the bump payload. Null unless this is a bump run.</summary>
    public PlutoniumBumpReleaseVersion? BumpVersion { get; }

    /// <summary>Gets the pull-request payload. Null unless this is a pull-request run.</summary>
    public PlutoniumPullRequestReleaseVersion? PullRequestVersion { get; }

    /// <summary>
    /// Gets the per-app output directory: the common output root, the lowercase
    /// AppId, then <c>runtime</c>. Files built there propagate to same-AppId
    /// test and publish entries.
    /// </summary>
    public PlutoniumPath OutputDirectory { get; }

    /// <summary>Gets whether this is a version bump run.</summary>
    public bool IsBump => BumpVersion is not null;

    /// <summary>Gets whether this is a pull request run.</summary>
    public bool IsPullRequest => PullRequestVersion is not null;

    /// <summary>Gets whether this is a local development run. True when the RunType flags include Local.</summary>
    public bool IsLocal => (RunType & PlutoniumRunType.Local) != 0;

    /// <summary>Gets whether this is a commit-based run. True when the RunType flags include Commit.</summary>
    public bool IsCommit => (RunType & PlutoniumRunType.Commit) != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppRunContext"/> class.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="runType">The run type flags.</param>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <param name="appVersion">The application version. Must not be null.</param>
    /// <param name="bumpVersion">The bump payload, if this is a bump run. Its AppId must match <paramref name="appId"/>.</param>
    /// <param name="pullRequestVersion">The pull-request payload, if this is a pull-request run. Its AppId must match <paramref name="appId"/>.</param>
    /// <exception cref="ArgumentException">Thrown when a version payload AppId does not match <paramref name="appId"/>.</exception>
    public AppRunContext(
        PlutoniumAppId appId,
        PlutoniumRunType runType,
        PlutoniumPath commonOutputDirectory,
        PlutoniumAppVersion appVersion,
        PlutoniumBumpReleaseVersion? bumpVersion = null,
        PlutoniumPullRequestReleaseVersion? pullRequestVersion = null)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        ArgumentNullException.ThrowIfNull(appVersion);
        if (!appVersion.AppId.Equals(appId))
        {
            throw new ArgumentException(
                $"AppVersion AppId '{appVersion.AppId}' does not match AppId '{appId}'.",
                nameof(appVersion));
        }

        if (bumpVersion is not null && !bumpVersion.AppId.Equals(appId))
        {
            throw new ArgumentException(
                $"BumpVersion AppId '{bumpVersion.AppId}' does not match AppId '{appId}'.",
                nameof(bumpVersion));
        }

        if (pullRequestVersion is not null && !pullRequestVersion.AppId.Equals(appId))
        {
            throw new ArgumentException(
                $"PullRequestVersion AppId '{pullRequestVersion.AppId}' does not match AppId '{appId}'.",
                nameof(pullRequestVersion));
        }

        AppId = appId;
        RunType = runType;
        AppVersion = appVersion;
        BumpVersion = bumpVersion;
        PullRequestVersion = pullRequestVersion;
        OutputDirectory = PlutoniumOutputLayout.OutputDirectoryFor(commonOutputDirectory, appId);
    }
}
