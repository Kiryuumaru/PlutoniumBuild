using System.Text.RegularExpressions;

namespace Plutonium.Helpers;

/// <summary>
/// Minimal strict SemVer value used for tag protocol parsing, precedence comparison,
/// and bump arithmetic. String labels stay the interchange format; this type is the
/// parsed view. A typed third-party SemVer library is deferred; this recreation
/// covers only what versioning needs: strict parse, precedence, and part setters.
/// </summary>
public sealed partial class PlutoniumSemVersion : IEquatable<PlutoniumSemVersion>, IComparable<PlutoniumSemVersion>
{
    /// <summary>Gets the major version component.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version component.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version component.</summary>
    public int Patch { get; }

    /// <summary>Gets the prerelease label without the leading dash, or empty for releases. Never null.</summary>
    public string Prerelease { get; }

    /// <summary>Gets the build metadata without the leading plus, or empty when absent. Never null. Ignored by precedence.</summary>
    public string BuildMetadata { get; }

    /// <summary>Gets whether this version carries a prerelease label.</summary>
    public bool IsPrerelease => Prerelease.Length != 0;

    /// <summary>Gets whether this version is the empty seed (0.0.0 in every part).</summary>
    public bool IsEmpty => Major == 0 && Minor == 0 && Patch == 0;

    /// <summary>Gets the dot-separated prerelease identifiers. Empty for releases. Never null.</summary>
    public IReadOnlyList<string> PrereleaseIdentifiers =>
        IsPrerelease ? Prerelease.Split('.') : [];

    /// <summary>Gets the precedence comparer: major, minor, patch, then prerelease rules (release beats prerelease).</summary>
    public static IComparer<PlutoniumSemVersion> PrecedenceComparer { get; } =
        Comparer<PlutoniumSemVersion>.Create(static (left, right) => Compare(left, right));

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumSemVersion"/> class.
    /// </summary>
    /// <param name="major">The major component. Must be zero or greater.</param>
    /// <param name="minor">The minor component. Must be zero or greater.</param>
    /// <param name="patch">The patch component. Must be zero or greater.</param>
    /// <param name="prerelease">The prerelease label without dash. Null or empty means release.</param>
    /// <param name="buildMetadata">The build metadata without plus. Null or empty means absent.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a component is negative.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="prerelease"/> or <paramref name="buildMetadata"/> is malformed.</exception>
    public PlutoniumSemVersion(int major, int minor, int patch, string? prerelease = null, string? buildMetadata = null)
    {
        if (major < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major), major, "Major must be zero or greater.");
        }

        if (minor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minor), minor, "Minor must be zero or greater.");
        }

        if (patch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(patch), patch, "Patch must be zero or greater.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = NormalizePrerelease(prerelease);
        BuildMetadata = NormalizeBuildMetadata(buildMetadata);
    }

    /// <summary>
    /// Parses a strict SemVer label (no leading zeros, prerelease and metadata validated).
    /// </summary>
    /// <param name="value">The label. Must not be null or whitespace.</param>
    /// <returns>The parsed version. Never null.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="value"/> is not strict SemVer.</exception>
    public static PlutoniumSemVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!TryParse(value, out var version) || version is null)
        {
            throw new FormatException($"Invalid strict SemVer version '{value}'.");
        }

        return version;
    }

    /// <summary>
    /// Tries to parse a strict SemVer label without throwing.
    /// </summary>
    /// <param name="value">The candidate label. May be null.</param>
    /// <param name="version">The parsed version when valid; otherwise null.</param>
    /// <returns>True when <paramref name="value"/> is strict SemVer.</returns>
    public static bool TryParse(string? value, out PlutoniumSemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = SemVerPattern().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        version = new PlutoniumSemVersion(
            int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            match.Groups[4].Success ? match.Groups[4].Value : null,
            match.Groups[5].Success ? match.Groups[5].Value : null);
        return true;
    }

    /// <summary>Gets whether the version is null or the empty seed.</summary>
    /// <param name="version">The version. May be null.</param>
    /// <returns>True when null or 0.0.0 in every part.</returns>
    public static bool IsNullOrEmpty(PlutoniumSemVersion? version) => version is null || version.IsEmpty;

    /// <summary>
    /// Compares two versions by precedence. Null sorts before any version.
    /// </summary>
    /// <param name="left">The left version. May be null.</param>
    /// <param name="right">The right version. May be null.</param>
    /// <returns>Negative when left precedes right, zero when equal, positive when left follows right.</returns>
    public static int Compare(PlutoniumSemVersion? left, PlutoniumSemVersion? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var compared = left.Major.CompareTo(right.Major);
        if (compared != 0)
        {
            return compared;
        }

        compared = left.Minor.CompareTo(right.Minor);
        if (compared != 0)
        {
            return compared;
        }

        compared = left.Patch.CompareTo(right.Patch);
        if (compared != 0)
        {
            return compared;
        }

        return ComparePrerelease(left.PrereleaseIdentifiers, right.PrereleaseIdentifiers);
    }

    /// <summary>
    /// Compares this version's precedence against another. Null sorts before any version.
    /// </summary>
    /// <param name="other">The other version. May be null.</param>
    /// <returns>Negative when this precedes other, zero when equal, positive when this follows other.</returns>
    public int ComparePrecedenceTo(PlutoniumSemVersion? other) => Compare(this, other);

    /// <inheritdoc/>
    public int CompareTo(PlutoniumSemVersion? other) => Compare(this, other);

    /// <summary>Returns a copy without build metadata. Precedence is unaffected by metadata, so grouping keys stay stable.</summary>
    /// <returns>The version without metadata. Never null.</returns>
    public PlutoniumSemVersion WithoutMetadata() => new(Major, Minor, Patch, Prerelease.Length == 0 ? null : Prerelease);

    /// <summary>Returns a copy with the major component replaced.</summary>
    /// <param name="major">The major component. Must be zero or greater.</param>
    /// <returns>The new version. Never null.</returns>
    public PlutoniumSemVersion WithMajor(int major) => new(major, Minor, Patch, Prerelease.Length == 0 ? null : Prerelease, BuildMetadata.Length == 0 ? null : BuildMetadata);

    /// <summary>Returns a copy with the minor component replaced.</summary>
    /// <param name="minor">The minor component. Must be zero or greater.</param>
    /// <returns>The new version. Never null.</returns>
    public PlutoniumSemVersion WithMinor(int minor) => new(Major, minor, Patch, Prerelease.Length == 0 ? null : Prerelease, BuildMetadata.Length == 0 ? null : BuildMetadata);

    /// <summary>Returns a copy with the patch component replaced.</summary>
    /// <param name="patch">The patch component. Must be zero or greater.</param>
    /// <returns>The new version. Never null.</returns>
    public PlutoniumSemVersion WithPatch(int patch) => new(Major, Minor, patch, Prerelease.Length == 0 ? null : Prerelease, BuildMetadata.Length == 0 ? null : BuildMetadata);

    /// <summary>Returns a copy with the prerelease label replaced. Null or empty clears it; metadata is preserved.</summary>
    /// <param name="prerelease">The prerelease label without dash, or null to clear.</param>
    /// <returns>The new version. Never null.</returns>
    public PlutoniumSemVersion WithPrerelease(string? prerelease) =>
        new(Major, Minor, Patch, prerelease, BuildMetadata.Length == 0 ? null : BuildMetadata);

    /// <summary>Returns an equal copy.</summary>
    /// <returns>The copy. Never null.</returns>
    public PlutoniumSemVersion Clone() =>
        new(Major, Minor, Patch, Prerelease.Length == 0 ? null : Prerelease, BuildMetadata.Length == 0 ? null : BuildMetadata);

    /// <inheritdoc/>
    public bool Equals(PlutoniumSemVersion? other) =>
        other is not null &&
        Major == other.Major &&
        Minor == other.Minor &&
        Patch == other.Patch &&
        string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal) &&
        string.Equals(BuildMetadata, other.BuildMetadata, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PlutoniumSemVersion);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease, BuildMetadata);

    /// <inheritdoc/>
    public override string ToString() =>
        Prerelease.Length == 0 && BuildMetadata.Length == 0
            ? $"{Major}.{Minor}.{Patch}"
            : Prerelease.Length == 0
                ? $"{Major}.{Minor}.{Patch}+{BuildMetadata}"
                : BuildMetadata.Length == 0
                    ? $"{Major}.{Minor}.{Patch}-{Prerelease}"
                    : $"{Major}.{Minor}.{Patch}-{Prerelease}+{BuildMetadata}";

    /// <summary>Compares two versions for value equality, including build metadata. Use <see cref="Compare"/> for precedence.</summary>
    /// <param name="left">The left version. May be null.</param>
    /// <param name="right">The right version. May be null.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(PlutoniumSemVersion? left, PlutoniumSemVersion? right) => Equals(left, right);

    /// <summary>Compares two versions for value inequality, including build metadata. Use <see cref="Compare"/> for precedence.</summary>
    /// <param name="left">The left version. May be null.</param>
    /// <param name="right">The right version. May be null.</param>
    /// <returns>True when not equal.</returns>
    public static bool operator !=(PlutoniumSemVersion? left, PlutoniumSemVersion? right) => !Equals(left, right);

    /// <summary>Gets whether the left version precedes the right version.</summary>
    /// <param name="left">The left version. May be null.</param>
    /// <param name="right">The right version. May be null.</param>
    /// <returns>True when left precedes right.</returns>
    public static bool operator <(PlutoniumSemVersion? left, PlutoniumSemVersion? right) => Compare(left, right) < 0;

    /// <summary>Gets whether the left version follows the right version.</summary>
    /// <param name="left">The left version. May be null.</param>
    /// <param name="right">The right version. May be null.</param>
    /// <returns>True when left follows right.</returns>
    public static bool operator >(PlutoniumSemVersion? left, PlutoniumSemVersion? right) => Compare(left, right) > 0;

    /// <summary>Gets whether the left version precedes or equals the right version.</summary>
    /// <param name="left">The left version. May be null.</param>
    /// <param name="right">The right version. May be null.</param>
    /// <returns>True when left precedes or equals right.</returns>
    public static bool operator <=(PlutoniumSemVersion? left, PlutoniumSemVersion? right) => Compare(left, right) <= 0;

    /// <summary>Gets whether the left version follows or equals the right version.</summary>
    /// <param name="left">The left version. May be null.</param>
    /// <param name="right">The right version. May be null.</param>
    /// <returns>True when left follows or equals right.</returns>
    public static bool operator >=(PlutoniumSemVersion? left, PlutoniumSemVersion? right) => Compare(left, right) >= 0;

    private static int ComparePrerelease(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 0;
        }

        if (left.Count == 0)
        {
            return 1;
        }

        if (right.Count == 0)
        {
            return -1;
        }

        var shared = Math.Min(left.Count, right.Count);
        for (var index = 0; index < shared; index++)
        {
            var compared = ComparePrereleaseIdentifier(left[index], right[index]);
            if (compared != 0)
            {
                return compared;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftNumeric = IsNumericIdentifier(left);
        var rightNumeric = IsNumericIdentifier(right);
        if (leftNumeric && rightNumeric)
        {
            var lengthCompared = left.Length.CompareTo(right.Length);
            return lengthCompared != 0
                ? lengthCompared
                : string.Compare(left, right, StringComparison.Ordinal);
        }

        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    private static bool IsNumericIdentifier(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePrerelease(string? prerelease)
    {
        if (string.IsNullOrEmpty(prerelease))
        {
            return string.Empty;
        }

        if (!PrereleasePattern().IsMatch(prerelease))
        {
            throw new FormatException($"Invalid prerelease label '{prerelease}'.");
        }

        return prerelease;
    }

    private static string NormalizeBuildMetadata(string? buildMetadata)
    {
        if (string.IsNullOrEmpty(buildMetadata))
        {
            return string.Empty;
        }

        if (!BuildMetadataPattern().IsMatch(buildMetadata))
        {
            throw new FormatException($"Invalid build metadata '{buildMetadata}'.");
        }

        return buildMetadata;
    }

    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemVerPattern();

    [GeneratedRegex(
        @"^(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PrereleasePattern();

    [GeneratedRegex(
        @"^[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex BuildMetadataPattern();
}
