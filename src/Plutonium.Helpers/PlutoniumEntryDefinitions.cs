using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Minimal workflow-builder handle passed to entry <c>DisplayName</c> and
/// <c>WorkflowBuilder</c> callbacks. Opaque in this phase; future workflow
/// emitters supply the implementation.
/// </summary>
public interface IPlutoniumWorkflowBuilder
{
}

/// <summary>
/// Shared entry-definition knobs: identity, AppIds, condition, RunnerOS, cache,
/// checkout, execute steps, workflow display, matrix fan-out, and run context.
/// </summary>
/// <remarks>
/// Inherit the <c>RunEntryDefinition</c> base (or a concrete <c>TestEntryDefinition</c> /
/// <c>BuildEntryDefinition</c> / <c>PublishEntryDefinition</c>); do not implement this
/// interface directly. Knob properties are <c>internal</c> so the same-named public fluent extensions
/// (the author surface) win overload resolution outside this assembly; inside,
/// discovery and runners work the properties directly.
/// </remarks>
public interface IRunEntryDefinition
{
    /// <summary>Gets or sets the entry id. Defaults to <c>_</c>; discovery replaces it with the property name.</summary>
    string Id { get; set; }

    /// <summary>Gets or sets the AppIds this entry runs for. At least one is required.</summary>
    List<PlutoniumAppId> AppIds { get; set; }

    /// <summary>Gets or sets how the entry renders its display name in workflows.</summary>
    internal Func<IPlutoniumWorkflowBuilder, Task<string>> DisplayName { get; set; }

    /// <summary>Gets or sets whether the entry runs. Defaults to true.</summary>
    internal Func<IRunContext, Task<bool>> Condition { get; set; }

    /// <summary>Gets or sets the RunnerOS selector. Null means the emitter default applies.</summary>
    internal Func<IRunContext, Task<PlutoniumRunnerOS>>? RunnerOS { get; set; }

    /// <summary>Gets or sets the cache-path providers. Defaults to empty.</summary>
    internal List<Func<IRunContext, Task<PlutoniumPath[]>>> CachePath { get; set; }

    /// <summary>Gets or sets the cache invalidator. Defaults to <c>0</c>; change it to bust the cache.</summary>
    internal Func<IRunContext, Task<string>> CacheInvalidator { get; set; }

    /// <summary>Gets or sets the checkout fetch depth. Defaults to 1; 0 fetches full history.</summary>
    internal Func<IRunContext, Task<int>> CheckoutFetchDepth { get; set; }

    /// <summary>Gets or sets whether checkout fetches tags even when depth is set. Defaults to false.</summary>
    internal Func<IRunContext, Task<bool>> CheckoutFetchTags { get; set; }

    /// <summary>Gets or sets how checkout handles submodules. Defaults to None.</summary>
    internal Func<IRunContext, Task<PlutoniumSubmoduleCheckoutType>> CheckoutSubmodules { get; set; }

    /// <summary>Gets or sets the execute steps run in order. Defaults to empty.</summary>
    internal List<Func<IRunContext, Task>> Execute { get; set; }

    /// <summary>Gets or sets the workflow-builder steps. Defaults to empty; they affect generated YAML only.</summary>
    internal List<Func<IPlutoniumWorkflowBuilder, Task>> WorkflowBuilder { get; set; }

    /// <summary>Gets or sets the matrix expanders. Defaults to empty; each fans one entry into many.</summary>
    internal List<Func<IRunEntryDefinition, Task<IRunEntryDefinition[]>>> Matrix { get; set; }

    /// <summary>Gets or sets the active run context. Set by the runner before execution.</summary>
    internal IRunContext? RunContext { get; set; }

    /// <summary>Clones the definition, copying every configured knob.</summary>
    /// <returns>The clone. Never null.</returns>
    IRunEntryDefinition Clone();

    /// <summary>Resolves the display name for the given workflow builder.</summary>
    /// <param name="workflowBuilder">The workflow builder. Must not be null.</param>
    /// <returns>The display name. Never null.</returns>
    Task<string> GetDisplayNameAsync(IPlutoniumWorkflowBuilder workflowBuilder);

    /// <summary>Evaluates the entry condition against the active run context.</summary>
    /// <returns>True when the entry runs.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no run context is set.</exception>
    Task<bool> GetConditionAsync();

    /// <summary>Resolves the RunnerOS against the active run context.</summary>
    /// <returns>The RunnerOS, or null when unset (emitter default applies).</returns>
    Task<PlutoniumRunnerOS?> GetRunnerOSAsync();

    /// <summary>Collects every cache path from all providers.</summary>
    /// <returns>The cache paths. Never null.</returns>
    Task<PlutoniumPath[]> GetCachePathsAsync();

    /// <summary>Resolves the cache invalidator.</summary>
    /// <returns>The invalidator. Never null.</returns>
    Task<string> GetCacheInvalidatorAsync();

    /// <summary>Resolves the checkout fetch depth.</summary>
    /// <returns>The fetch depth.</returns>
    Task<int> GetCheckoutFetchDepthAsync();

    /// <summary>Resolves whether checkout fetches tags.</summary>
    /// <returns>True when tags are fetched.</returns>
    Task<bool> GetCheckoutFetchTagsAsync();

    /// <summary>Resolves how checkout handles submodules.</summary>
    /// <returns>The submodule mode.</returns>
    Task<PlutoniumSubmoduleCheckoutType> GetCheckoutSubmodulesAsync();

    /// <summary>Runs every execute step in order against the active run context.</summary>
    /// <returns>A task for the execution.</returns>
    Task ExecuteAsync();

    /// <summary>Runs every workflow-builder step in order.</summary>
    /// <param name="workflowBuilder">The workflow builder. Must not be null.</param>
    /// <returns>A task for the execution.</returns>
    Task BuildWorkflowAsync(IPlutoniumWorkflowBuilder workflowBuilder);
}

/// <summary>Marker for dependent entries (test entries depending on builds).</summary>
public interface IDependentEntryDefinition : IRunEntryDefinition
{
}

/// <summary>Marker for target-backed entries (build and publish).</summary>
public interface ITargetEntryDefinition : IRunEntryDefinition
{
}

/// <summary>Build entry definition. Files created on the app-specific output directory propagate to same-AppId test and publish entries.</summary>
public interface IBuildEntryDefinition : ITargetEntryDefinition
{
}

/// <summary>Test entry definition.</summary>
public interface ITestEntryDefinition : IDependentEntryDefinition
{
    /// <summary>Gets or sets whether the test runs before the build within an app. Defaults to false.</summary>
    internal Func<Task<bool>> ExecuteBeforeBuild { get; set; }

    /// <summary>Resolves whether the test runs before the build.</summary>
    /// <returns>True when the test orders before the build.</returns>
    Task<bool> GetExecuteBeforeBuildAsync();
}

/// <summary>Publish entry definition.</summary>
public interface IPublishEntryDefinition : ITargetEntryDefinition
{
}

/// <summary>
/// Workflow-config definition: the six pipeline knobs (name, pre/post setup OSes,
/// asset-hash append, prerelease on release, JSON file versioning).
/// </summary>
/// <remarks>
/// Inherit <c>WorkflowConfigEntryDefinition</c>; do not implement this interface directly.
/// Knob properties are <c>internal</c> for the same reason as
/// <see cref="IRunEntryDefinition"/>: the public fluent extensions are the author surface.
/// </remarks>
public interface IWorkflowConfigEntryDefinition
{
    /// <summary>Gets or sets the pipeline display name. Defaults to <c>Plutonium CICD Pipeline</c>.</summary>
    internal Func<Task<string>> Name { get; set; }

    /// <summary>Gets or sets the pre-setup RunnerOS. Defaults to Ubuntu2204.</summary>
    internal Func<Task<PlutoniumRunnerOS>> PreSetupRunnerOS { get; set; }

    /// <summary>Gets or sets the post-setup RunnerOS. Defaults to Ubuntu2204.</summary>
    internal Func<Task<PlutoniumRunnerOS>> PostSetupRunnerOS { get; set; }

    /// <summary>Gets or sets whether release-asset hashes are appended to release notes. Defaults to true.</summary>
    internal Func<Task<bool>> AppendReleaseNotesAssetHashes { get; set; }

    /// <summary>Gets or sets whether releases off the main branch are prereleases. Defaults to true.</summary>
    internal Func<Task<bool>> EnablePrereleaseOnRelease { get; set; }

    /// <summary>Gets or sets whether JSON file versioning is used. Defaults to false.</summary>
    internal Func<Task<bool>> UseJsonFileVersioning { get; set; }

    /// <summary>Clones the definition, copying every configured knob.</summary>
    /// <returns>The clone. Never null.</returns>
    IWorkflowConfigEntryDefinition Clone();

    /// <summary>Resolves the pipeline display name.</summary>
    /// <returns>The name. Never null.</returns>
    Task<string> GetNameAsync();

    /// <summary>Resolves the pre-setup RunnerOS.</summary>
    /// <returns>The RunnerOS. Never null.</returns>
    Task<PlutoniumRunnerOS> GetPreSetupRunnerOSAsync();

    /// <summary>Resolves the post-setup RunnerOS.</summary>
    /// <returns>The RunnerOS. Never null.</returns>
    Task<PlutoniumRunnerOS> GetPostSetupRunnerOSAsync();

    /// <summary>Resolves whether asset hashes are appended to release notes.</summary>
    /// <returns>True when hashes are appended.</returns>
    Task<bool> GetAppendReleaseNotesAssetHashesAsync();

    /// <summary>Resolves whether off-main releases are prereleases.</summary>
    /// <returns>True when prereleases are enabled.</returns>
    Task<bool> GetEnablePrereleaseOnReleaseAsync();

    /// <summary>Resolves whether JSON file versioning is used.</summary>
    /// <returns>True when JSON versioning is enabled.</returns>
    Task<bool> GetUseJsonFileVersioningAsync();
}
