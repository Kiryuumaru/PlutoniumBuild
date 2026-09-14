using Plutonium.Core;

namespace Plutonium.Helpers;

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

/// <summary>
/// Options scoping one <see cref="PlutoniumAllVersions"/> reconciliation: which tags
/// to read, which branches count as environments, and how versions-file state merges.
/// Paths stay strings here; callers resolve them to <see cref="PlutoniumPath"/>.
/// </summary>
public sealed class PlutoniumVersionsOptions
{
    /// <summary>Gets the raw <c>git ls-remote --tags</c> output lines. Never null.</summary>
    public IReadOnlyList<string> LsRemoteLines { get; }

    /// <summary>Gets the environment branches. Never null or empty; entries must not be null or whitespace.</summary>
    public IReadOnlyList<string> EnvironmentBranches { get; }

    /// <summary>Gets the main environment branch. Never null or whitespace.</summary>
    public string MainEnvironmentBranch { get; }

    /// <summary>Gets the HEAD commit reconciled against tag commits. Never null or whitespace.</summary>
    public string HeadCommit { get; }

    /// <summary>Gets whether JSON file versioning merges file versions into the taxonomy. Defaults to false.</summary>
    public bool UseJsonFileVersioning { get; init; }

    /// <summary>Gets the raw versions-file map for merging. Null unless <see cref="UseJsonFileVersioning"/> applies.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? VersionsFileRaw { get; init; }

    /// <summary>Gets the known lowercase AppIds for versions-file validation. Never null.</summary>
    public IReadOnlyCollection<string> KnownAppIds { get; init; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumVersionsOptions"/> class.
    /// </summary>
    /// <param name="lsRemoteLines">The raw ls-remote lines. Must not be null; entries must not be null.</param>
    /// <param name="environmentBranches">The environment branches. Must not be null or empty.</param>
    /// <param name="mainEnvironmentBranch">The main branch. Must not be null or whitespace.</param>
    /// <param name="headCommit">The HEAD commit. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when branches or the HEAD commit are missing.</exception>
    public PlutoniumVersionsOptions(
        IReadOnlyList<string> lsRemoteLines,
        IReadOnlyList<string> environmentBranches,
        string mainEnvironmentBranch,
        string headCommit)
    {
        ArgumentNullException.ThrowIfNull(lsRemoteLines);
        foreach (var line in lsRemoteLines)
        {
            ArgumentNullException.ThrowIfNull(line, nameof(lsRemoteLines));
        }

        ArgumentNullException.ThrowIfNull(environmentBranches);
        if (environmentBranches.Count == 0)
        {
            throw new ArgumentException("At least one environment branch is required.", nameof(environmentBranches));
        }

        foreach (var branch in environmentBranches)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(branch, nameof(environmentBranches));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mainEnvironmentBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(headCommit);
        LsRemoteLines = lsRemoteLines;
        EnvironmentBranches = environmentBranches;
        MainEnvironmentBranch = mainEnvironmentBranch.Trim();
        HeadCommit = headCommit.Trim();
    }
}

/// <summary>
/// Versioning reconciliation: parses <c>git ls-remote --tags</c> output into the
/// <see cref="PlutoniumAllVersions"/> taxonomy for one AppId, validates environment
/// branches, validates bumps, and fetches tags through <see cref="PlutoniumGit"/>.
/// Pure parsing works from raw lines so workflows and CLI verbs can test without git.
/// </summary>
public static class PlutoniumVersions
{
    private const string RefsTagsPrefix = "refs/tags/";

    /// <summary>
    /// Validates environment branches: lowercased, no duplicates, and the main branch present.
    /// </summary>
    /// <param name="environmentBranches">The environment branches. Must not be null.</param>
    /// <param name="mainEnvironmentBranch">The main branch. Must not be null or whitespace.</param>
    /// <exception cref="InvalidOperationException">Thrown on duplicates or a missing main branch.</exception>
    public static void ValidateEnvironmentBranches(IEnumerable<string> environmentBranches, string mainEnvironmentBranch)
    {
        ArgumentNullException.ThrowIfNull(environmentBranches);
        ArgumentException.ThrowIfNullOrWhiteSpace(mainEnvironmentBranch);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in environmentBranches)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(branch, nameof(environmentBranches));
            var lowered = branch.Trim().ToLowerInvariant();
            if (!seen.Add(lowered))
            {
                throw new InvalidOperationException($"Duplicate environment branch '{lowered}'.");
            }
        }

        if (!seen.Contains(mainEnvironmentBranch.Trim().ToLowerInvariant()))
        {
            throw new InvalidOperationException(
                $"Main environment branch '{mainEnvironmentBranch.Trim()}' is missing from the environment branches.");
        }
    }

    /// <summary>
    /// Reconciles raw ls-remote lines into the taxonomy for one AppId.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="options">The reconciliation options. Must not be null.</param>
    /// <returns>The taxonomy. Never null.</returns>
    public static PlutoniumAllVersions GetAllVersions(PlutoniumAppId appId, PlutoniumVersionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentNullException.ThrowIfNull(options);
        ValidateEnvironmentBranches(options.EnvironmentBranches, options.MainEnvironmentBranch);

        var mainLower = options.MainEnvironmentBranch.Trim().ToLowerInvariant();
        var envSet = new HashSet<string>(
            options.EnvironmentBranches.Select(branch => branch.Trim().ToLowerInvariant()), StringComparer.Ordinal);
        var appPrefix = appId.Value.ToLowerInvariant() + "/";

        var commitBuildIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var commitVersions = new Dictionary<string, List<PlutoniumSemVersion>>(StringComparer.Ordinal);
        var commitLatestTags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var buildIdCommits = new Dictionary<string, string>(StringComparer.Ordinal);
        var versionCommits = new Dictionary<PlutoniumSemVersion, string>();
        var envBuildIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var bump = new List<PlutoniumSemVersion>();
        var queue = new List<PlutoniumSemVersion>();
        var failed = new List<PlutoniumSemVersion>();
        var passed = new List<PlutoniumSemVersion>();

        foreach (var line in options.LsRemoteLines)
        {
            var peel = line.IndexOf(RefsTagsPrefix, StringComparison.Ordinal);
            if (peel < 0)
            {
                continue;
            }

            var commit = line[..peel].Trim();
            var rawTag = line[(peel + RefsTagsPrefix.Length)..].Trim();
            if (commit.Length == 0 || rawTag.Length == 0)
            {
                continue;
            }

            if (PlutoniumVersionTags.TryParseBuildTag(rawTag, out var buildId, out var buildEnv) && buildId is not null)
            {
                if (buildEnv is not null && !envSet.Contains(buildEnv))
                {
                    continue;
                }

                AddUnique(commitBuildIds, commit, buildId);
                if (!buildIdCommits.ContainsKey(buildId))
                {
                    buildIdCommits[buildId] = commit;
                }

                if (buildEnv is not null)
                {
                    AddUnique(envBuildIds, buildEnv, buildId);
                }

                continue;
            }

            if (!rawTag.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = rawTag[appPrefix.Length..];
            if (PlutoniumVersionTags.IsLatestRemainder(remainder, out var latestEnv))
            {
                AddUnique(commitLatestTags, commit, remainder);
                continue;
            }

            if (!PlutoniumVersionTags.TryParseVersionRemainder(
                remainder, out var version, out var isBump, out var isQueue, out var isFailed, out var isPassed) ||
                version is null)
            {
                continue;
            }

            var bare = version.WithoutMetadata();
            AddUnique(commitVersions, commit, bare);
            versionCommits[bare] = commit;
            if (isBump)
            {
                bump.Add(bare);
            }

            if (isQueue)
            {
                queue.Add(bare);
            }

            if (isFailed)
            {
                failed.Add(bare);
            }

            if (isPassed)
            {
                passed.Add(bare);
            }
        }

        var allVersions = commitVersions.Values.SelectMany(versions => versions).ToList();
        foreach (var environment in options.EnvironmentBranches)
        {
            var lowered = environment.Trim().ToLowerInvariant();
            allVersions.Add(lowered.Equals(mainLower, StringComparison.Ordinal)
                ? new PlutoniumSemVersion(0, 0, 0)
                : new PlutoniumSemVersion(0, 0, 0, lowered + ".0"));
        }

        var envVersions = allVersions
            .GroupBy(version => version.IsPrerelease ? version.PrereleaseIdentifiers[0].ToLowerInvariant() : mainLower)
            .ToDictionary(
                group => group.Key,
                group => (List<PlutoniumSemVersion>)group.ToList(),
                StringComparer.Ordinal);

        var fileMap = new Dictionary<string, PlutoniumSemVersion>(StringComparer.Ordinal);
        if (options.UseJsonFileVersioning && options.VersionsFileRaw is not null)
        {
            var known = options.KnownAppIds.Count == 0
                ? new[] { appId.Value.ToLowerInvariant() }
                : options.KnownAppIds;
            try
            {
                var appFile = PlutoniumVersionsFile.ReadApp(options.VersionsFileRaw, appId.Value, known);
                foreach (var pair in appFile.EnvironmentVersions)
                {
                    fileMap[pair.Key.ToLowerInvariant()] = pair.Value;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (options.UseJsonFileVersioning && fileMap.Count != 0)
        {
            foreach (var environment in options.EnvironmentBranches)
            {
                var lowered = environment.Trim().ToLowerInvariant();
                if (fileMap.TryGetValue(lowered, out var fileVersion) &&
                    envVersions.TryGetValue(lowered, out var grouped) &&
                    !allVersions.Contains(fileVersion))
                {
                    AddUnique(commitVersions, options.HeadCommit, fileVersion);
                    bump.Add(fileVersion);
                    allVersions.Add(fileVersion);
                    grouped.Add(fileVersion);
                }
            }
        }

        foreach (var grouped in envVersions.Values)
        {
            grouped.Sort(PlutoniumSemVersion.PrecedenceComparer);
        }

        var latestByEnv = PairLatest(commitLatestTags, commitVersions, envBuildIds, mainLower);
        var sorted = envVersions.Keys.ToList();
        sorted.Sort(StringComparer.Ordinal);
        if (sorted.Remove(mainLower))
        {
            sorted.Add(mainLower);
        }

        return new PlutoniumAllVersions(
            FreezeStrings(commitBuildIds),
            FreezeVersions(commitVersions),
            FreezeStrings(commitLatestTags),
            versionCommits,
            buildIdCommits,
            FreezeVersions(envVersions),
            FreezeStrings(envBuildIds),
            latestByEnv.ToDictionary(pair => pair.Key, pair => pair.Value.Version, StringComparer.Ordinal),
            latestByEnv.ToDictionary(pair => pair.Key, pair => pair.Value.BuildId, StringComparer.Ordinal),
            sorted,
            bump,
            queue,
            failed,
            passed,
            fileMap);
    }

    /// <summary>
    /// Fetches remote tags through Core git and reconciles them. Throws when git fails.
    /// </summary>
    /// <param name="git">The Core git surface. Must not be null.</param>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="remoteUrl">The remote URL. Must not be null or whitespace.</param>
    /// <param name="environmentBranches">The environment branches. Must not be null.</param>
    /// <param name="mainEnvironmentBranch">The main branch. Must not be null or whitespace.</param>
    /// <param name="headCommit">The HEAD commit. Must not be null or whitespace.</param>
    /// <param name="useJsonFileVersioning">Whether file versions merge into the taxonomy.</param>
    /// <param name="versionsFileRaw">The raw file map for merging. May be null.</param>
    /// <param name="knownAppIds">Known lowercase AppIds. May be null.</param>
    /// <param name="workingDirectory">The working directory, or null to inherit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The taxonomy. Never null.</returns>
    public static async Task<PlutoniumAllVersions> GetAllVersionsAsync(
        PlutoniumGit git,
        PlutoniumAppId appId,
        string remoteUrl,
        IReadOnlyList<string> environmentBranches,
        string mainEnvironmentBranch,
        string headCommit,
        bool useJsonFileVersioning = false,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? versionsFileRaw = null,
        IReadOnlyCollection<string>? knownAppIds = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);

        var result = await git.ListRemoteTagsAsync(remoteUrl, workingDirectory, cancellationToken).ConfigureAwait(false);
        result.EnsureSucceeded();
        var lines = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return GetAllVersions(appId, new PlutoniumVersionsOptions(lines, environmentBranches, mainEnvironmentBranch, headCommit)
        {
            UseJsonFileVersioning = useJsonFileVersioning,
            VersionsFileRaw = versionsFileRaw,
            KnownAppIds = knownAppIds ?? [],
        });
    }

    /// <summary>
    /// Validates a bump candidate: strict SemVer, prerelease bound to the current branch,
    /// releases only on the main branch, and ahead of the per-environment latest.
    /// </summary>
    /// <param name="allVersions">The reconciled taxonomy. Must not be null.</param>
    /// <param name="candidate">The bump candidate label. Must not be null or whitespace.</param>
    /// <param name="currentBranch">The current branch. Must not be null or whitespace.</param>
    /// <param name="mainEnvironmentBranch">The main branch. Must not be null or whitespace.</param>
    /// <returns>The parsed candidate. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the branch binding or ordering rule fails.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="candidate"/> is not strict SemVer.</exception>
    public static PlutoniumSemVersion ValidateBumpVersion(
        PlutoniumAllVersions allVersions,
        string candidate,
        string currentBranch,
        string mainEnvironmentBranch)
    {
        ArgumentNullException.ThrowIfNull(allVersions);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(mainEnvironmentBranch);

        if (!PlutoniumSemVersion.TryParse(candidate.Trim(), out var input) || input is null)
        {
            throw new FormatException("Invalid strict SemVer version.");
        }

        string environment;
        if (input.IsPrerelease)
        {
            var prereleaseBranch = input.PrereleaseIdentifiers[0];
            if (!currentBranch.Trim().Equals(prereleaseBranch, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Version {input} must bump on the '{prereleaseBranch}' branch.");
            }

            environment = prereleaseBranch.ToLowerInvariant();
        }
        else
        {
            if (!currentBranch.Trim().Equals(mainEnvironmentBranch.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Version {input} must bump on the '{mainEnvironmentBranch.Trim().ToLowerInvariant()}' branch.");
            }

            environment = mainEnvironmentBranch.Trim().ToLowerInvariant();
        }

        if (allVersions.EnvVersionGrouped.TryGetValue(environment, out var grouped) && grouped.Count != 0)
        {
            var latest = grouped[^1];
            var compared = latest.ComparePrecedenceTo(input);
            if (compared == 0)
            {
                throw new InvalidOperationException(
                    $"The latest version in the {environment} releases is already {input}.");
            }

            if (compared > 0)
            {
                throw new InvalidOperationException(
                    $"Version {input} trails the latest version {latest} in the {environment} releases.");
            }
        }

        return input;
    }

    /// <summary>
    /// Maps a trigger onto run types: pull requests stay pull requests, local stays local,
    /// commits stay commits unless versions-file mode marks released entries as bumps, and
    /// tags become bumps for released entries else commits.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="hasRelease">Whether the entry carries a release for this run.</param>
    /// <param name="useJsonFileVersioning">Whether JSON file versioning applies.</param>
    /// <returns>The run type.</returns>
    /// <exception cref="NotSupportedException">Thrown for unknown triggers.</exception>
    public static PlutoniumRunType ToRunType(PlutoniumTriggerType trigger, bool hasRelease, bool useJsonFileVersioning) =>
        trigger switch
        {
            PlutoniumTriggerType.PullRequest => PlutoniumRunType.PullRequest,
            PlutoniumTriggerType.Commit => useJsonFileVersioning && hasRelease ? PlutoniumRunType.Bump : PlutoniumRunType.Commit,
            PlutoniumTriggerType.Tag => hasRelease ? PlutoniumRunType.Bump : PlutoniumRunType.Commit,
            PlutoniumTriggerType.Local => PlutoniumRunType.Local,
            _ => throw new NotSupportedException($"Trigger '{trigger}' is not supported."),
        };

    private static void AddUnique<TValue>(Dictionary<string, List<TValue>> map, string key, TValue value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }

    private static Dictionary<string, (string BuildId, PlutoniumSemVersion Version)> PairLatest(
        IReadOnlyDictionary<string, List<string>> commitLatestTags,
        IReadOnlyDictionary<string, List<PlutoniumSemVersion>> commitVersions,
        IReadOnlyDictionary<string, List<string>> envBuildIds,
        string mainLower)
    {
        var paired = new Dictionary<string, (string BuildId, PlutoniumSemVersion Version)>(StringComparer.Ordinal);
        foreach (var entry in commitLatestTags)
        {
            if (!commitVersions.TryGetValue(entry.Key, out var versions) || versions.Count == 0)
            {
                continue;
            }

            foreach (var latestTag in entry.Value)
            {
                if (latestTag.Equals(PlutoniumVersionTags.LatestMarker, StringComparison.OrdinalIgnoreCase))
                {
                    if (!envBuildIds.TryGetValue(mainLower, out var buildIds) || buildIds.Count == 0)
                    {
                        continue;
                    }

                    var latest = versions
                        .Where(version => !version.IsPrerelease)
                        .MaxBy(version => version, PlutoniumSemVersion.PrecedenceComparer);
                    if (latest is not null)
                    {
                        paired[mainLower] = (MaxBuildId(buildIds)!, latest);
                    }
                }
                else if (latestTag.StartsWith(
                    PlutoniumVersionTags.LatestMarker + "-", StringComparison.OrdinalIgnoreCase))
                {
                    var environment = latestTag[(PlutoniumVersionTags.LatestMarker.Length + 1)..].ToLowerInvariant();
                    if (environment.Length == 0)
                    {
                        continue;
                    }

                    if (!envBuildIds.TryGetValue(environment, out var buildIds) || buildIds.Count == 0)
                    {
                        continue;
                    }

                    var latest = versions
                        .Where(version =>
                            version.IsPrerelease &&
                            version.PrereleaseIdentifiers[0].Equals(environment, StringComparison.OrdinalIgnoreCase))
                        .MaxBy(version => version, PlutoniumSemVersion.PrecedenceComparer);
                    if (latest is not null)
                    {
                        paired[environment] = (MaxBuildId(buildIds)!, latest);
                    }
                }
            }
        }

        return paired;
    }

    private static string MaxBuildId(IReadOnlyList<string> buildIds)
    {
        var best = buildIds[0];
        for (var index = 1; index < buildIds.Count; index++)
        {
            if (PlutoniumVersionTags.CompareBuildIds(buildIds[index], best) > 0)
            {
                best = buildIds[index];
            }
        }

        return best;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> FreezeStrings(
        Dictionary<string, List<string>> map) =>
        map.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, IReadOnlyList<PlutoniumSemVersion>> FreezeVersions(
        Dictionary<string, List<PlutoniumSemVersion>> map) =>
        map.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlutoniumSemVersion>)pair.Value.AsReadOnly(), StringComparer.Ordinal);
}
