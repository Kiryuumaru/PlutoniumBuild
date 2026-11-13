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
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Versioning;
/// <summary>
/// One application's version file: the AppId plus per-environment strict versions.
/// Serializes as <c>{appid: {env: version}}</c> inside <see cref="PlutoniumAllVersionsFile"/>.
/// </summary>
public sealed class PlutoniumVersionFile
{
    /// <summary>Gets the application identifier. Never null or whitespace.</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; }

    /// <summary>Gets the per-environment versions keyed by lowercase environment. Never null.</summary>
    [JsonPropertyName("environmentVersions")]
    public IReadOnlyDictionary<string, PlutoniumSemVersion> EnvironmentVersions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumVersionFile"/> class.
    /// </summary>
    /// <param name="appId">The application identifier. Must not be null or whitespace.</param>
    /// <param name="environmentVersions">The per-environment versions. Must not be null; keys must not be null or whitespace and values must not be null.</param>
    /// <exception cref="ArgumentException">Thrown when a key is null or whitespace.</exception>
    public PlutoniumVersionFile(string appId, IReadOnlyDictionary<string, PlutoniumSemVersion> environmentVersions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(environmentVersions);
        foreach (var pair in environmentVersions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key, nameof(environmentVersions));
            ArgumentNullException.ThrowIfNull(pair.Value, nameof(environmentVersions));
        }

        AppId = appId.Trim();
        EnvironmentVersions = environmentVersions;
    }
}
