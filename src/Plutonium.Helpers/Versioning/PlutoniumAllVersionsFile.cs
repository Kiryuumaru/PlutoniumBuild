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
/// The <c>versions.json</c> document: every application's <see cref="PlutoniumVersionFile"/>.
/// </summary>
public sealed class PlutoniumAllVersionsFile
{
    /// <summary>Gets the per-application version files keyed by lowercase AppId. Never null.</summary>
    [JsonPropertyName("versions")]
    public IReadOnlyDictionary<string, PlutoniumVersionFile> Versions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumAllVersionsFile"/> class.
    /// </summary>
    /// <param name="versions">The version files. Must not be null; values must not be null.</param>
    public PlutoniumAllVersionsFile(IReadOnlyDictionary<string, PlutoniumVersionFile> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        foreach (var pair in versions)
        {
            ArgumentNullException.ThrowIfNull(pair.Value, nameof(versions));
        }

        Versions = versions;
    }
}
