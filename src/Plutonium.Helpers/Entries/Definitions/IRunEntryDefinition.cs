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
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Entries.Definitions;
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
