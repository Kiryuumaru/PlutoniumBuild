using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Pipeline the run executes on. Helpers-own; mapped from <see cref="PlutoniumHostKind"/>.
/// </summary>
public enum PlutoniumPipelineType
{
    /// <summary>Azure Pipelines.</summary>
    Azure = 0,

    /// <summary>GitHub Actions.</summary>
    Github = 1,

    /// <summary>Local developer machine; no CI host detected.</summary>
    Local = 2,
}

/// <summary>
/// Maps the Core host detection onto the Helpers pipeline type.
/// </summary>
public static class PlutoniumPipelineTypeExtensions
{
    /// <summary>
    /// Maps a detected Core host kind onto the Helpers pipeline type.
    /// </summary>
    /// <param name="kind">The detected host kind.</param>
    /// <returns>The pipeline type. Never null.</returns>
    public static PlutoniumPipelineType ToPipelineType(this PlutoniumHostKind kind) => kind switch
    {
        PlutoniumHostKind.GitHubActions => PlutoniumPipelineType.Github,
        PlutoniumHostKind.AzurePipelines => PlutoniumPipelineType.Azure,
        _ => PlutoniumPipelineType.Local,
    };
}

/// <summary>
/// Kind of run an entry executes for.
/// </summary>
[Flags]
public enum PlutoniumRunType
{
    /// <summary>No run type selected.</summary>
    None = 0,

    /// <summary>Local run only, excluding pipeline execution.</summary>
    Local = 1,

    /// <summary>Run triggered by a pull request.</summary>
    PullRequest = 2,

    /// <summary>Run triggered by a commit on environment branches, excluding pull requests.</summary>
    Commit = 4,

    /// <summary>Run triggered by a version bump.</summary>
    Bump = 8,

    /// <summary>Run for the target app entry only if it is also running.</summary>
    Target = 16,
}

/// <summary>
/// What triggered the run. Consumed by future workflow emitters; local runs report Local.
/// </summary>
public enum PlutoniumTriggerType
{
    /// <summary>Triggered by a pull request.</summary>
    PullRequest = 0,

    /// <summary>Triggered by a tag push.</summary>
    Tag = 1,

    /// <summary>Triggered by a branch commit.</summary>
    Commit = 2,

    /// <summary>Local developer run.</summary>
    Local = 3,
}

/// <summary>
/// Version information for one application in one environment.
/// The version stays a string label in this phase; typed SemVer lands with versioning.
/// </summary>
public class PlutoniumAppVersion
{
    /// <summary>Gets the application identifier. Never null.</summary>
    public PlutoniumAppId AppId { get; }

    /// <summary>Gets the environment the version applies to. Never null or whitespace.</summary>
    public string Environment { get; }

    /// <summary>Gets the version label. Never null or whitespace.</summary>
    public string Version { get; }

    /// <summary>Gets the build identifier associated with the version. Never null or whitespace.</summary>
    public string BuildId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumAppVersion"/> class.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="environment">The environment. Must not be null or whitespace.</param>
    /// <param name="version">The version label. Must not be null or whitespace.</param>
    /// <param name="buildId">The build identifier. Must not be null or whitespace.</param>
    public PlutoniumAppVersion(PlutoniumAppId appId, string environment, string version, string buildId)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        AppId = appId;
        Environment = environment.Trim();
        Version = version.Trim();
        BuildId = buildId.Trim();
    }
}

/// <summary>
/// A bumped release version carrying release notes. Set only on bump runs.
/// </summary>
public sealed class PlutoniumBumpReleaseVersion : PlutoniumAppVersion
{
    /// <summary>Gets the release notes for this bump. Never null.</summary>
    public string ReleaseNotes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumBumpReleaseVersion"/> class.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="environment">The environment. Must not be null or whitespace.</param>
    /// <param name="version">The version label. Must not be null or whitespace.</param>
    /// <param name="buildId">The build identifier. Must not be null or whitespace.</param>
    /// <param name="releaseNotes">The release notes. Must not be null.</param>
    public PlutoniumBumpReleaseVersion(
        PlutoniumAppId appId,
        string environment,
        string version,
        string buildId,
        string releaseNotes)
        : base(appId, environment, version, buildId)
    {
        ArgumentNullException.ThrowIfNull(releaseNotes);
        ReleaseNotes = releaseNotes;
    }
}

/// <summary>
/// A release version tied to a pull request. Set only on pull-request runs.
/// </summary>
public sealed class PlutoniumPullRequestReleaseVersion : PlutoniumAppVersion
{
    /// <summary>Gets the pull request number.</summary>
    public long PullRequestNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumPullRequestReleaseVersion"/> class.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="environment">The environment. Must not be null or whitespace.</param>
    /// <param name="version">The version label. Must not be null or whitespace.</param>
    /// <param name="buildId">The build identifier. Must not be null or whitespace.</param>
    /// <param name="pullRequestNumber">The pull request number.</param>
    public PlutoniumPullRequestReleaseVersion(
        PlutoniumAppId appId,
        string environment,
        string version,
        string buildId,
        long pullRequestNumber)
        : base(appId, environment, version, buildId)
    {
        PullRequestNumber = pullRequestNumber;
    }
}

/// <summary>
/// Per-run execution context handed to every <c>Execute</c>, <c>Condition</c>,
/// and <c>RunnerOS</c> lambda. Multi-app runs see all apps in <see cref="Apps"/>.
/// </summary>
public interface IRunContext
{
    /// <summary>Gets the pipeline kind for this run.</summary>
    PlutoniumPipelineType PipelineType { get; }

    /// <summary>Gets the per-app contexts keyed by AppId.</summary>
    IReadOnlyDictionary<PlutoniumAppId, AppRunContext> Apps { get; }
}

/// <summary>
/// Default <see cref="IRunContext"/> implementation used for local and pipeline runs.
/// </summary>
public sealed class PlutoniumRunContext : IRunContext
{
    /// <summary>Gets the pipeline kind for this run.</summary>
    public PlutoniumPipelineType PipelineType { get; }

    /// <summary>Gets the per-app contexts keyed by AppId.</summary>
    public IReadOnlyDictionary<PlutoniumAppId, AppRunContext> Apps { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumRunContext"/> class.
    /// </summary>
    /// <param name="pipelineType">The pipeline kind.</param>
    /// <param name="apps">The per-app contexts. Must not be null; keys must match each context's AppId.</param>
    /// <exception cref="ArgumentException">Thrown when a key does not match its context's AppId.</exception>
    public PlutoniumRunContext(
        PlutoniumPipelineType pipelineType,
        IReadOnlyDictionary<PlutoniumAppId, AppRunContext> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        foreach (var pair in apps)
        {
            ArgumentNullException.ThrowIfNull(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!pair.Key.Equals(pair.Value.AppId))
            {
                throw new ArgumentException(
                    $"Apps key '{pair.Key}' does not match context AppId '{pair.Value.AppId}'.",
                    nameof(apps));
            }
        }

        PipelineType = pipelineType;
        Apps = apps;
    }
}

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
