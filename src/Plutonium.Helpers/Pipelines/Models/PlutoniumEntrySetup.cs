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
/// Per-entry setup captured during pre-setup: run types, condition, RunnerOS,
/// cache, and checkout knobs. Serialized into the pre-setup payload.
/// </summary>
public sealed class PlutoniumEntrySetup
{
    /// <summary>Gets the entry id. Never null.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets the run type per lowercase AppId.</summary>
    [JsonPropertyName("run_types")]
    public Dictionary<string, PlutoniumRunType> RunTypes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets whether the entry runs.</summary>
    [JsonPropertyName("condition")]
    public bool Condition { get; set; }

    /// <summary>Gets the RunnerOS setup. Never null.</summary>
    [JsonPropertyName("runner_os")]
    public PlutoniumRunnerSetup RunnerOSSetup { get; set; } = new("ubuntu-22.04", "{}", "chmod +x ./build.sh && ./build.sh");

    /// <summary>Gets the cache invalidator. Never null.</summary>
    [JsonPropertyName("cache_invalidator")]
    public string CacheInvalidator { get; set; } = "0";

    /// <summary>Gets the cache paths relative to the repository root. Never null.</summary>
    [JsonPropertyName("cache_paths")]
    public List<string> CachePaths { get; set; } = [];

    /// <summary>Gets the checkout fetch depth.</summary>
    [JsonPropertyName("checkout_fetch_depth")]
    public int CheckoutFetchDepth { get; set; } = 1;

    /// <summary>Gets whether checkout fetches tags.</summary>
    [JsonPropertyName("checkout_fetch_tags")]
    public bool CheckoutFetchTags { get; set; }

    /// <summary>Gets how checkout handles submodules.</summary>
    [JsonPropertyName("checkout_submodules")]
    public PlutoniumSubmoduleCheckoutType CheckoutSubmodules { get; set; }
}
