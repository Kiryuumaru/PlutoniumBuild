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
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Versioning;
/// <summary>
/// Reconciled version state for one AppId: commit groupings, environment groupings,
/// latest markers, bump/queue/failed/passed taxonomy, and the versions-file map.
/// String labels from <see cref="PlutoniumAppVersion"/> stay the interchange format;
/// this taxonomy is the parsed view computed from remote tags.
/// </summary>
public sealed class PlutoniumAllVersions
{
    /// <summary>Gets build identifiers grouped by commit. Never null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CommitBuildIdGrouped { get; }

    /// <summary>Gets versions grouped by commit. Never null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>> CommitVersionGrouped { get; }

    /// <summary>Gets latest-marker remainders grouped by commit. Never null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CommitLatestTagGrouped { get; }

    /// <summary>Gets the commit carrying each version. Never null.</summary>
    public IReadOnlyDictionary<PlutoniumSemVersion, string> VersionCommitPaired { get; }

    /// <summary>Gets the commit carrying each build identifier. Never null.</summary>
    public IReadOnlyDictionary<string, string> BuildIdCommitPaired { get; }

    /// <summary>Gets ascending versions grouped by lowercase environment. Never null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>> EnvVersionGrouped { get; }

    /// <summary>Gets build identifiers grouped by lowercase environment. Never null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> EnvBuildIdGrouped { get; }

    /// <summary>Gets the latest version per lowercase environment from latest markers. Never null.</summary>
    public IReadOnlyDictionary<string, PlutoniumSemVersion> EnvLatestVersionPaired { get; }

    /// <summary>Gets the latest build identifier per lowercase environment from latest markers. Never null.</summary>
    public IReadOnlyDictionary<string, string> EnvLatestBuildIdPaired { get; }

    /// <summary>Gets environments sorted ascending with the main branch last. Never null.</summary>
    public IReadOnlyList<string> EnvSorted { get; }

    /// <summary>Gets versions carrying the bump marker. Never null.</summary>
    public IReadOnlyList<PlutoniumSemVersion> VersionBump { get; }

    /// <summary>Gets versions carrying the queue marker. Never null.</summary>
    public IReadOnlyList<PlutoniumSemVersion> VersionQueue { get; }

    /// <summary>Gets versions carrying the failed marker. Never null.</summary>
    public IReadOnlyList<PlutoniumSemVersion> VersionFailed { get; }

    /// <summary>Gets versions carrying the passed marker. Never null.</summary>
    public IReadOnlyList<PlutoniumSemVersion> VersionPassed { get; }

    /// <summary>Gets file versions keyed by lowercase environment. Never null.</summary>
    public IReadOnlyDictionary<string, PlutoniumSemVersion> EnvVersionFileMap { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumAllVersions"/> class.
    /// </summary>
    /// <param name="commitBuildIdGrouped">Build identifiers by commit. Must not be null.</param>
    /// <param name="commitVersionGrouped">Versions by commit. Must not be null.</param>
    /// <param name="commitLatestTagGrouped">Latest markers by commit. Must not be null.</param>
    /// <param name="versionCommitPaired">Commit per version. Must not be null.</param>
    /// <param name="buildIdCommitPaired">Commit per build identifier. Must not be null.</param>
    /// <param name="envVersionGrouped">Ascending versions per lowercase environment. Must not be null.</param>
    /// <param name="envBuildIdGrouped">Build identifiers per lowercase environment. Must not be null.</param>
    /// <param name="envLatestVersionPaired">Latest version per lowercase environment. Must not be null.</param>
    /// <param name="envLatestBuildIdPaired">Latest build identifier per lowercase environment. Must not be null.</param>
    /// <param name="envSorted">Environments sorted with main last. Must not be null.</param>
    /// <param name="versionBump">Bump-marked versions. Must not be null.</param>
    /// <param name="versionQueue">Queue-marked versions. Must not be null.</param>
    /// <param name="versionFailed">Failed-marked versions. Must not be null.</param>
    /// <param name="versionPassed">Passed-marked versions. Must not be null.</param>
    /// <param name="envVersionFileMap">File versions per lowercase environment. Must not be null.</param>
    public PlutoniumAllVersions(
        IReadOnlyDictionary<string, IReadOnlyList<string>> commitBuildIdGrouped,
        IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>> commitVersionGrouped,
        IReadOnlyDictionary<string, IReadOnlyList<string>> commitLatestTagGrouped,
        IReadOnlyDictionary<PlutoniumSemVersion, string> versionCommitPaired,
        IReadOnlyDictionary<string, string> buildIdCommitPaired,
        IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>> envVersionGrouped,
        IReadOnlyDictionary<string, IReadOnlyList<string>> envBuildIdGrouped,
        IReadOnlyDictionary<string, PlutoniumSemVersion> envLatestVersionPaired,
        IReadOnlyDictionary<string, string> envLatestBuildIdPaired,
        IReadOnlyList<string> envSorted,
        IReadOnlyList<PlutoniumSemVersion> versionBump,
        IReadOnlyList<PlutoniumSemVersion> versionQueue,
        IReadOnlyList<PlutoniumSemVersion> versionFailed,
        IReadOnlyList<PlutoniumSemVersion> versionPassed,
        IReadOnlyDictionary<string, PlutoniumSemVersion> envVersionFileMap)
    {
        ArgumentNullException.ThrowIfNull(commitBuildIdGrouped);
        ArgumentNullException.ThrowIfNull(commitVersionGrouped);
        ArgumentNullException.ThrowIfNull(commitLatestTagGrouped);
        ArgumentNullException.ThrowIfNull(versionCommitPaired);
        ArgumentNullException.ThrowIfNull(buildIdCommitPaired);
        ArgumentNullException.ThrowIfNull(envVersionGrouped);
        ArgumentNullException.ThrowIfNull(envBuildIdGrouped);
        ArgumentNullException.ThrowIfNull(envLatestVersionPaired);
        ArgumentNullException.ThrowIfNull(envLatestBuildIdPaired);
        ArgumentNullException.ThrowIfNull(envSorted);
        ArgumentNullException.ThrowIfNull(versionBump);
        ArgumentNullException.ThrowIfNull(versionQueue);
        ArgumentNullException.ThrowIfNull(versionFailed);
        ArgumentNullException.ThrowIfNull(versionPassed);
        ArgumentNullException.ThrowIfNull(envVersionFileMap);
        CommitBuildIdGrouped = commitBuildIdGrouped;
        CommitVersionGrouped = commitVersionGrouped;
        CommitLatestTagGrouped = commitLatestTagGrouped;
        VersionCommitPaired = versionCommitPaired;
        BuildIdCommitPaired = buildIdCommitPaired;
        EnvVersionGrouped = envVersionGrouped;
        EnvBuildIdGrouped = envBuildIdGrouped;
        EnvLatestVersionPaired = envLatestVersionPaired;
        EnvLatestBuildIdPaired = envLatestBuildIdPaired;
        EnvSorted = envSorted;
        VersionBump = versionBump;
        VersionQueue = versionQueue;
        VersionFailed = versionFailed;
        VersionPassed = versionPassed;
        EnvVersionFileMap = envVersionFileMap;
    }
}
