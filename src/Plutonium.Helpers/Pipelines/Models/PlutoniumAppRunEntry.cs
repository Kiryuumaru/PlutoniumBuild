using System.Text.Json;
using System.Text.Json.Serialization;
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
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Pipelines.Models;
/// <summary>
/// One application's pre-setup outcome: environment, versions, and whether it carries a release.
/// </summary>
public sealed class PlutoniumAppRunEntry
{
    /// <summary>Gets the application identifier. Never null.</summary>
    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets the lowercase environment. Never null.</summary>
    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>Gets the version label with build metadata. Never null.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets the previously released version label. Never null.</summary>
    [JsonPropertyName("old_version")]
    public string OldVersion { get; set; } = string.Empty;

    /// <summary>Gets whether this app carries a release for this run.</summary>
    [JsonPropertyName("has_release")]
    public bool HasRelease { get; set; }
}
