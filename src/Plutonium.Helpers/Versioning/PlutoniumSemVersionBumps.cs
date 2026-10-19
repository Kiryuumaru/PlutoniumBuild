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
/// Bump arithmetic over <see cref="PlutoniumSemVersion"/>: strict empty check and
/// ordered <see cref="PlutoniumVersionBump"/> application. Major/minor/patch move
/// their part and reset lower parts plus the prerelease counter; prerelease moves
/// the numeric tail. A typed third-party SemVer library stays deferred.
/// </summary>
public static class PlutoniumSemVersionBumps
{
    /// <summary>Gets whether the version is null or the empty seed. Never throws.</summary>
    /// <param name="version">The version. May be null.</param>
    /// <returns>True when null or 0.0.0 in every part.</returns>
    public static bool IsNullOrEmpty(PlutoniumSemVersion? version) => PlutoniumSemVersion.IsNullOrEmpty(version);

    /// <summary>
    /// Applies ordered bumps to a version. Callers sort by rank; this method applies in sequence.
    /// </summary>
    /// <param name="version">The base version. Must not be null.</param>
    /// <param name="bumps">The bumps in apply order. Must not be null; entries must not be null.</param>
    /// <returns>The bumped version. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a prerelease bump targets a version without a dotted numeric prerelease tail.</exception>
    public static PlutoniumSemVersion ApplyBumps(PlutoniumSemVersion version, IEnumerable<PlutoniumVersionBump> bumps)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(bumps);

        var current = version.WithoutMetadata();
        foreach (var bump in bumps)
        {
            ArgumentNullException.ThrowIfNull(bump, nameof(bumps));
            current = bump.Part switch
            {
                PlutoniumVersionPart.Major => ApplyMajor(current, bump),
                PlutoniumVersionPart.Minor => ApplyMinor(current, bump),
                PlutoniumVersionPart.Patch => ApplyPatch(current, bump),
                PlutoniumVersionPart.Prerelease => ApplyPrerelease(current, bump),
                _ => throw new NotSupportedException($"Version part '{bump.Part}' is not supported."),
            };
        }

        return current;
    }

    private static PlutoniumSemVersion ApplyMajor(PlutoniumSemVersion version, PlutoniumVersionBump bump)
    {
        var major = bump.IsIncrement ? checked(version.Major + bump.BumpAssign) : bump.BumpAssign;
        var next = version.WithMajor(major).WithMinor(0).WithPatch(0);
        return ResetPrereleaseCounter(next);
    }

    private static PlutoniumSemVersion ApplyMinor(PlutoniumSemVersion version, PlutoniumVersionBump bump)
    {
        var minor = bump.IsIncrement ? checked(version.Minor + bump.BumpAssign) : bump.BumpAssign;
        var next = version.WithMinor(minor).WithPatch(0);
        return ResetPrereleaseCounter(next);
    }

    private static PlutoniumSemVersion ApplyPatch(PlutoniumSemVersion version, PlutoniumVersionBump bump)
    {
        var patch = bump.IsIncrement ? checked(version.Patch + bump.BumpAssign) : bump.BumpAssign;
        return ResetPrereleaseCounter(version.WithPatch(patch));
    }

    private static PlutoniumSemVersion ApplyPrerelease(PlutoniumSemVersion version, PlutoniumVersionBump bump)
    {
        var identifiers = version.PrereleaseIdentifiers;
        if (identifiers.Count < 2 || !int.TryParse(identifiers[^1], out var counter))
        {
            throw new InvalidOperationException(
                $"Prerelease bump requires a dotted numeric tail (for example 'prerelease.4'); version is '{version}'.");
        }

        var next = bump.IsIncrement ? checked(counter + bump.BumpAssign) : bump.BumpAssign;
        var head = string.Join(".", identifiers.Take(identifiers.Count - 1));
        return version.WithPrerelease(head + "." + next.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static PlutoniumSemVersion ResetPrereleaseCounter(PlutoniumSemVersion version)
    {
        if (!version.IsPrerelease)
        {
            return version;
        }

        var head = version.PrereleaseIdentifiers[0];
        return version.WithPrerelease(head + ".1");
    }
}
