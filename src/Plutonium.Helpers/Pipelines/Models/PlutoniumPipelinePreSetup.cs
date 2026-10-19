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
/// Pre-setup payload owned by Helpers: branch, trigger, build ids, entry lists,
/// per-entry setups, and per-app outcomes. Transported as base64 JSON via
/// <see cref="PlutoniumPipelineContract"/> under <c>PLUTONIUM_PRE_SETUP</c>.
/// </summary>
public sealed class PlutoniumPipelinePreSetup
{
    /// <summary>Gets or sets the branch name. Never null.</summary>
    [JsonPropertyName("branch")]
    public string Branch { get; set; } = string.Empty;

    /// <summary>Gets or sets what triggered the run.</summary>
    [JsonPropertyName("trigger_type")]
    public PlutoniumTriggerType TriggerType { get; set; }

    /// <summary>Gets or sets the lowercase environment.</summary>
    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>Gets or sets the build identifier. Never null.</summary>
    [JsonPropertyName("build_id")]
    public string BuildId { get; set; } = string.Empty;

    /// <summary>Gets or sets the previous successful build identifier. Never null.</summary>
    [JsonPropertyName("last_build_id")]
    public string LastBuildId { get; set; } = string.Empty;

    /// <summary>Gets or sets the pull-request number.</summary>
    [JsonPropertyName("pull_request_number")]
    public long PullRequestNumber { get; set; }

    /// <summary>Gets or sets whether any app carries a release.</summary>
    [JsonPropertyName("has_release")]
    public bool HasRelease { get; set; }

    /// <summary>Gets or sets the test entry ids. Never null.</summary>
    [JsonPropertyName("test_entries")]
    public List<string> TestEntries { get; set; } = [];

    /// <summary>Gets or sets the build entry ids. Never null.</summary>
    [JsonPropertyName("build_entries")]
    public List<string> BuildEntries { get; set; } = [];

    /// <summary>Gets or sets the publish entry ids. Never null.</summary>
    [JsonPropertyName("publish_entries")]
    public List<string> PublishEntries { get; set; } = [];

    /// <summary>Gets or sets the per-entry setups keyed by entry id. Never null.</summary>
    [JsonPropertyName("entry_setup_map")]
    public Dictionary<string, PlutoniumEntrySetup> EntrySetupMap { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the per-app outcomes keyed by lowercase AppId. Never null.</summary>
    [JsonPropertyName("app_run_entry_map")]
    public Dictionary<string, PlutoniumAppRunEntry> AppRunEntryMap { get; set; } = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    /// <summary>
    /// Serializes this payload to compact JSON text.
    /// </summary>
    /// <returns>JSON text. Never null.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, PayloadOptions);

    /// <summary>
    /// Deserializes a payload from JSON text.
    /// </summary>
    /// <param name="json">JSON text. Must not be null or whitespace.</param>
    /// <returns>The payload. Never null.</returns>
    public static PlutoniumPipelinePreSetup FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var parsed = JsonSerializer.Deserialize<PlutoniumPipelinePreSetup>(json.Trim(), PayloadOptions);
        ArgumentNullException.ThrowIfNull(parsed);
        return parsed;
    }

    /// <summary>
    /// Encodes this payload for transport in an environment variable.
    /// </summary>
    /// <returns>Base64 payload. Never null.</returns>
    public string ToPayload() => PlutoniumPipelineContract.EncodePayload(ToJson());

    /// <summary>
    /// Decodes a payload from its base64 transport form.
    /// </summary>
    /// <param name="payload">Base64 payload. Must not be null or whitespace.</param>
    /// <returns>The payload. Never null.</returns>
    public static PlutoniumPipelinePreSetup FromPayload(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        return FromJson(PlutoniumPipelineContract.DecodePayload(payload));
    }

    /// <summary>
    /// Reads the payload from the environment.
    /// </summary>
    /// <param name="variableName">The variable name. Defaults to <c>PLUTONIUM_PRE_SETUP</c>.</param>
    /// <returns>The payload. Never null.</returns>
    public static PlutoniumPipelinePreSetup FromEnvironment(string variableName = PlutoniumPipelineContract.PayloadVariableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        return FromJson(PlutoniumPipelineContract.ReadFromEnvironment(variableName));
    }
}
