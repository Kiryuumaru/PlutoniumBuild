using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Helpers-side output and artifact layout: per-app directories under a shared
/// output root, the <c>$common</c> pseudo-app for shared outputs, and the
/// <c>___</c> artifact-name separator. Core stays path-opaque; only string
/// conventions live here.
/// </summary>
public static class PlutoniumOutputLayout
{
    /// <summary>Gets the pseudo-app carrying shared outputs across apps.</summary>
    public const string CommonAppId = "$common";

    /// <summary>Gets the separator joining app and artifact names in packed zips.</summary>
    public const string ArtifactNameSeparator = "___";

    /// <summary>Gets the leaf directory holding per-app runtime outputs.</summary>
    public const string RuntimeDirectoryName = "runtime";

    /// <summary>
    /// Builds the per-app output directory: common root, lowercase AppId, then <c>runtime</c>.
    /// </summary>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <returns>The per-app output directory. Never null.</returns>
    public static PlutoniumPath OutputDirectoryFor(PlutoniumPath commonOutputDirectory, PlutoniumAppId appId)
    {
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        ArgumentNullException.ThrowIfNull(appId);
        return commonOutputDirectory.Combine(appId.Value.ToLowerInvariant(), RuntimeDirectoryName);
    }

    /// <summary>
    /// Builds the shared <c>$common</c> output directory under the common root.
    /// </summary>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <returns>The shared output directory. Never null.</returns>
    public static PlutoniumPath CommonOutputDirectoryFor(PlutoniumPath commonOutputDirectory)
    {
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        return commonOutputDirectory.Combine(CommonAppId, RuntimeDirectoryName);
    }

    /// <summary>
    /// Builds a packed artifact file name joining the app and artifact names.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null.</param>
    /// <param name="artifactName">The artifact name. Must not be null or whitespace.</param>
    /// <returns>The file name (for example <c>myapp___logs.zip</c>). Never null.</returns>
    public static string ArtifactFileName(PlutoniumAppId appId, string artifactName)
    {
        ArgumentNullException.ThrowIfNull(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        return appId.Value.ToLowerInvariant() + ArtifactNameSeparator + artifactName.Trim() + ".zip";
    }
}
