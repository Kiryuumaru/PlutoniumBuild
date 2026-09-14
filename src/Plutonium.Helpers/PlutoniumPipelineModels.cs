using System.Text.Json;
using System.Text.Json.Serialization;
using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Detected pipeline info: the branch, what triggered the run, and the pull-request number.
/// Parsed from CI refs with a git-branch fallback; local runs report Local.
/// </summary>
public sealed class PlutoniumPipelineInfo
{
    /// <summary>Gets the branch name. Never null or whitespace.</summary>
    public string Branch { get; }

    /// <summary>Gets what triggered the run.</summary>
    public PlutoniumTriggerType TriggerType { get; }

    /// <summary>Gets the pull-request number. Zero unless the trigger is a pull request.</summary>
    public long PullRequestNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumPipelineInfo"/> class.
    /// </summary>
    /// <param name="branch">The branch name. Must not be null or whitespace.</param>
    /// <param name="triggerType">What triggered the run.</param>
    /// <param name="pullRequestNumber">The pull-request number. Zero unless pull request.</param>
    public PlutoniumPipelineInfo(string branch, PlutoniumTriggerType triggerType, long pullRequestNumber = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        Branch = branch.Trim();
        TriggerType = triggerType;
        PullRequestNumber = pullRequestNumber;
    }
}

/// <summary>
/// RunnerOS setup captured for one entry: display name, opaque pipeline payload, and run script.
/// </summary>
public sealed class PlutoniumRunnerSetup
{
    /// <summary>Gets the runner name (for example <c>ubuntu-22.04</c>). Never null.</summary>
    public string Name { get; }

    /// <summary>Gets the opaque pipeline OS payload as JSON. Never null.</summary>
    public string RunnerPipelineOS { get; }

    /// <summary>Gets the run script invoking the build. Never null.</summary>
    public string RunScript { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumRunnerSetup"/> class.
    /// </summary>
    /// <param name="name">The runner name. Must not be null or whitespace.</param>
    /// <param name="runnerPipelineOS">The opaque pipeline OS payload. Must not be null.</param>
    /// <param name="runScript">The run script. Must not be null or whitespace.</param>
    public PlutoniumRunnerSetup(string name, string runnerPipelineOS, string runScript)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(runnerPipelineOS);
        ArgumentException.ThrowIfNullOrWhiteSpace(runScript);
        Name = name.Trim();
        RunnerPipelineOS = runnerPipelineOS;
        RunScript = runScript.Trim();
    }
}

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
