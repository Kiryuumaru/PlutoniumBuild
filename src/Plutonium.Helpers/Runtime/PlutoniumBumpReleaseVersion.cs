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
