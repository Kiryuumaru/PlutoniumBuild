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
