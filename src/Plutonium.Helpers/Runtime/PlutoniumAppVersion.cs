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
