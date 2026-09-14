namespace Plutonium.Helpers;

/// <summary>
/// Tag protocol for AppId-keyed SemVer release management: version tags
/// <c>{appid-lower}/{semver}[-bump|-queue|-passed|-failed]</c>, latest markers
/// <c>{appid}/latest[-env]</c>, build markers <c>build.{id}[-env]</c>, and the
/// <c>bump-{branch}</c> trigger tag. All parsing is case-insensitive on the
/// AppId and suffixes; the version itself stays strict SemVer.
/// </summary>
public static class PlutoniumVersionTags
{
    /// <summary>Gets the bump marker suffix.</summary>
    public const string BumpSuffix = "-bump";

    /// <summary>Gets the queue marker suffix.</summary>
    public const string QueueSuffix = "-queue";

    /// <summary>Gets the passed marker suffix.</summary>
    public const string PassedSuffix = "-passed";

    /// <summary>Gets the failed marker suffix.</summary>
    public const string FailedSuffix = "-failed";

    /// <summary>Gets the latest marker infix.</summary>
    public const string LatestMarker = "latest";

    /// <summary>Gets the build marker prefix.</summary>
    public const string BuildMarker = "build.";

    /// <summary>Gets the bump trigger tag prefix.</summary>
    public const string BumpTriggerPrefix = "bump-";

    /// <summary>
    /// Builds a version tag: lowercase AppId, slash, version, optional marker suffix.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="version">The version. Must not be null.</param>
    /// <param name="suffix">The marker suffix (for example <c>-bump</c>), or null for the plain tag.</param>
    /// <returns>The tag. Never null.</returns>
    public static string VersionTag(PlutoniumAppId appId, PlutoniumSemVersion version, string? suffix = null)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentNullException.ThrowIfNull(version);
        return appId.Value.ToLowerInvariant() + "/" + version.ToString() + (suffix ?? string.Empty);
    }

    /// <summary>
    /// Builds the bump trigger tag for a branch: <c>bump-{branch-lower}</c>.
    /// </summary>
    /// <param name="branch">The branch. Must not be null or whitespace.</param>
    /// <returns>The trigger tag. Never null.</returns>
    public static string BumpTriggerTag(string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        return BumpTriggerPrefix + branch.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Builds a build marker tag: <c>build.{id}[-env-lower]</c>.
    /// </summary>
    /// <param name="buildId">The build identifier. Must not be null or whitespace.</param>
    /// <param name="environment">The environment, or null for the unqualified marker.</param>
    /// <returns>The tag. Never null.</returns>
    public static string BuildTag(string buildId, string? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        var tag = BuildMarker + buildId.Trim();
        return string.IsNullOrWhiteSpace(environment) ? tag : tag + "-" + environment.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Builds a latest marker tag: <c>{appid-lower}/latest[-env-lower]</c>.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="environment">The environment, or null for the main-branch marker.</param>
    /// <returns>The tag. Never null.</returns>
    public static string LatestTag(PlutoniumAppId appId, string? environment = null)
    {
        ArgumentNullException.ThrowIfNull(appId);
        var tag = appId.Value.ToLowerInvariant() + "/" + LatestMarker;
        return string.IsNullOrWhiteSpace(environment) ? tag : tag + "-" + environment.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Tries to split a raw tag into the AppId prefix and the remainder after the slash.
    /// </summary>
    /// <param name="rawTag">The raw tag. May be null.</param>
    /// <param name="appId">The AppId prefix when matched; otherwise null.</param>
    /// <param name="remainder">The remainder after the slash when matched; otherwise null.</param>
    /// <returns>True when the tag carries a slash-separated prefix.</returns>
    public static bool TrySplitAppTag(string? rawTag, out string? appId, out string? remainder)
    {
        appId = null;
        remainder = null;
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return false;
        }

        var slash = rawTag.IndexOf('/');
        if (slash <= 0 || slash == rawTag.Length - 1)
        {
            return false;
        }

        appId = rawTag[..slash];
        remainder = rawTag[(slash + 1)..];
        return true;
    }

    /// <summary>
    /// Tries to parse a version remainder (the part after <c>{appid}/</c>) into the
    /// strict version plus which marker suffixes were present.
    /// </summary>
    /// <param name="remainder">The remainder. May be null.</param>
    /// <param name="version">The parsed version when valid; otherwise null.</param>
    /// <param name="isBump">True when the <c>-bump</c> suffix was present.</param>
    /// <param name="isQueue">True when the <c>-queue</c> suffix was present.</param>
    /// <param name="isFailed">True when the <c>-failed</c> suffix was present.</param>
    /// <param name="isPassed">True when the <c>-passed</c> suffix was present.</param>
    /// <returns>True when the remainder holds strict SemVer plus known suffixes.</returns>
    public static bool TryParseVersionRemainder(
        string? remainder,
        out PlutoniumSemVersion? version,
        out bool isBump,
        out bool isQueue,
        out bool isFailed,
        out bool isPassed)
    {
        version = null;
        isBump = isQueue = isFailed = isPassed = false;
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var candidate = remainder.Trim();
        var stripped = true;
        while (stripped)
        {
            stripped = false;
            if (candidate.EndsWith(BumpSuffix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^BumpSuffix.Length];
                isBump = true;
                stripped = true;
            }
            else if (candidate.EndsWith(QueueSuffix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^QueueSuffix.Length];
                isQueue = true;
                stripped = true;
            }
            else if (candidate.EndsWith(FailedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^FailedSuffix.Length];
                isFailed = true;
                stripped = true;
            }
            else if (candidate.EndsWith(PassedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^PassedSuffix.Length];
                isPassed = true;
                stripped = true;
            }
        }

        return PlutoniumSemVersion.TryParse(candidate, out version) && version is not null;
    }

    /// <summary>
    /// Gets whether the remainder is a latest marker (<c>latest[-env]</c>).
    /// </summary>
    /// <param name="remainder">The remainder. May be null.</param>
    /// <param name="environment">The environment after the dash, or null for the main marker.</param>
    /// <returns>True when the remainder is a latest marker.</returns>
    public static bool IsLatestRemainder(string? remainder, out string? environment)
    {
        environment = null;
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var candidate = remainder.Trim();
        if (candidate.Equals(LatestMarker, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (candidate.StartsWith(LatestMarker + "-", StringComparison.OrdinalIgnoreCase))
        {
            environment = candidate[(LatestMarker.Length + 1)..].ToLowerInvariant();
            return environment.Length != 0;
        }

        return false;
    }

    /// <summary>
    /// Tries to parse a build marker tag (<c>build.{id}[-env]</c>).
    /// </summary>
    /// <param name="rawTag">The raw tag. May be null.</param>
    /// <param name="buildId">The build identifier when matched; otherwise null.</param>
    /// <param name="environment">The environment when qualified; otherwise null.</param>
    /// <returns>True when the tag is a build marker.</returns>
    public static bool TryParseBuildTag(string? rawTag, out string? buildId, out string? environment)
    {
        buildId = null;
        environment = null;
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return false;
        }

        var candidate = rawTag.Trim();
        if (!candidate.StartsWith(BuildMarker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = candidate[BuildMarker.Length..];
        if (rest.Length == 0)
        {
            return false;
        }

        var dash = rest.IndexOf('-');
        if (dash < 0)
        {
            buildId = rest;
            return buildId.Length != 0;
        }

        buildId = rest[..dash];
        environment = rest[(dash + 1)..].ToLowerInvariant();
        return buildId.Length != 0 && environment.Length != 0;
    }

    /// <summary>
    /// Compares two build identifiers: new timestamp-plus-hash stamps
    /// (<c>yyyyMMddHHmmss.hash</c>) sort after legacy numeric ids; two new stamps
    /// compare by timestamp then hash; two legacy ids compare numerically when
    /// possible, ordinally otherwise. The <c>build.</c> prefix is ignored.
    /// </summary>
    /// <param name="left">The left build identifier. May be null.</param>
    /// <param name="right">The right build identifier. May be null.</param>
    /// <returns>Negative when left sorts first, zero when equal, positive when left sorts last.</returns>
    public static int CompareBuildIds(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(left))
        {
            return -1;
        }

        if (string.IsNullOrEmpty(right))
        {
            return 1;
        }

        var strippedLeft = StripBuildPrefix(left);
        var strippedRight = StripBuildPrefix(right);
        var leftNew = IsNewBuildStamp(strippedLeft);
        var rightNew = IsNewBuildStamp(strippedRight);

        if (leftNew && rightNew)
        {
            var dateCompared = string.Compare(
                strippedLeft[..14], strippedRight[..14], StringComparison.Ordinal);
            return dateCompared != 0
                ? dateCompared
                : string.Compare(strippedLeft, strippedRight, StringComparison.Ordinal);
        }

        if (!leftNew && !rightNew)
        {
            if (int.TryParse(strippedLeft, out var leftNumber) &&
                int.TryParse(strippedRight, out var rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            return string.Compare(strippedLeft, strippedRight, StringComparison.Ordinal);
        }

        return leftNew ? 1 : -1;
    }

    private static string StripBuildPrefix(string value) =>
        value.StartsWith(BuildMarker, StringComparison.OrdinalIgnoreCase) ? value[BuildMarker.Length..] : value;

    private static bool IsNewBuildStamp(string value) =>
        value.Length >= 15 &&
        long.TryParse(value.AsSpan(0, 14), out _) &&
        value[14] == '.' &&
        value.Length > 15;
}
