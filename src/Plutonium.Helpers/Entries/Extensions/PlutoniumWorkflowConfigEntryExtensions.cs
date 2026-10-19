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
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Entries.Extensions;
/// <summary>Workflow-config extensions: the six pipeline knobs.</summary>
public static class PlutoniumWorkflowConfigEntryExtensions
{
    /// <summary>Sets a constant pipeline display name.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="name">The name. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition Name<TDefinition>(this TDefinition definition, string name)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(name);
        definition.Name = () => Task.FromResult(name);
        return definition;
    }

    /// <summary>Sets the pipeline display name from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="name">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition Name<TDefinition>(this TDefinition definition, Func<string> name)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(name);
        definition.Name = () => Task.FromResult(name());
        return definition;
    }

    /// <summary>Sets the pipeline display name from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="name">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition Name<TDefinition>(this TDefinition definition, Func<Task<string>> name)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(name);
        definition.Name = name;
        return definition;
    }

    /// <summary>Sets a constant pre-setup RunnerOS.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The RunnerOS. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PreSetupRunnerOS<TDefinition>(this TDefinition definition, PlutoniumRunnerOS runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PreSetupRunnerOS = () => Task.FromResult(runnerOS);
        return definition;
    }

    /// <summary>Sets the pre-setup RunnerOS from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PreSetupRunnerOS<TDefinition>(this TDefinition definition, Func<PlutoniumRunnerOS> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PreSetupRunnerOS = () => Task.FromResult(runnerOS());
        return definition;
    }

    /// <summary>Sets the pre-setup RunnerOS from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PreSetupRunnerOS<TDefinition>(this TDefinition definition, Func<Task<PlutoniumRunnerOS>> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PreSetupRunnerOS = runnerOS;
        return definition;
    }

    /// <summary>Sets a constant post-setup RunnerOS.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The RunnerOS. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PostSetupRunnerOS<TDefinition>(this TDefinition definition, PlutoniumRunnerOS runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PostSetupRunnerOS = () => Task.FromResult(runnerOS);
        return definition;
    }

    /// <summary>Sets the post-setup RunnerOS from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PostSetupRunnerOS<TDefinition>(this TDefinition definition, Func<PlutoniumRunnerOS> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PostSetupRunnerOS = () => Task.FromResult(runnerOS());
        return definition;
    }

    /// <summary>Sets the post-setup RunnerOS from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PostSetupRunnerOS<TDefinition>(this TDefinition definition, Func<Task<PlutoniumRunnerOS>> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PostSetupRunnerOS = runnerOS;
        return definition;
    }

    /// <summary>Sets whether release-asset hashes are appended to release notes.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="appendReleaseNotesAssetHashes">True to append hashes.</param>
    /// <returns>The definition.</returns>
    public static TDefinition AppendReleaseNotesAssetHashes<TDefinition>(this TDefinition definition, bool appendReleaseNotesAssetHashes)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.AppendReleaseNotesAssetHashes = () => Task.FromResult(appendReleaseNotesAssetHashes);
        return definition;
    }

    /// <summary>Sets asset-hash append from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="appendReleaseNotesAssetHashes">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition AppendReleaseNotesAssetHashes<TDefinition>(this TDefinition definition, Func<bool> appendReleaseNotesAssetHashes)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(appendReleaseNotesAssetHashes);
        definition.AppendReleaseNotesAssetHashes = () => Task.FromResult(appendReleaseNotesAssetHashes());
        return definition;
    }

    /// <summary>Sets asset-hash append from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="appendReleaseNotesAssetHashes">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition AppendReleaseNotesAssetHashes<TDefinition>(this TDefinition definition, Func<Task<bool>> appendReleaseNotesAssetHashes)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(appendReleaseNotesAssetHashes);
        definition.AppendReleaseNotesAssetHashes = appendReleaseNotesAssetHashes;
        return definition;
    }

    /// <summary>Sets whether off-main releases are marked prerelease.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="enablePrereleaseOnRelease">True to enable prereleases.</param>
    /// <returns>The definition.</returns>
    public static TDefinition EnablePrereleaseOnRelease<TDefinition>(this TDefinition definition, bool enablePrereleaseOnRelease)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.EnablePrereleaseOnRelease = () => Task.FromResult(enablePrereleaseOnRelease);
        return definition;
    }

    /// <summary>Sets prerelease-on-release from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="enablePrereleaseOnRelease">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition EnablePrereleaseOnRelease<TDefinition>(this TDefinition definition, Func<bool> enablePrereleaseOnRelease)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(enablePrereleaseOnRelease);
        definition.EnablePrereleaseOnRelease = () => Task.FromResult(enablePrereleaseOnRelease());
        return definition;
    }

    /// <summary>Sets prerelease-on-release from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="enablePrereleaseOnRelease">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition EnablePrereleaseOnRelease<TDefinition>(this TDefinition definition, Func<Task<bool>> enablePrereleaseOnRelease)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(enablePrereleaseOnRelease);
        definition.EnablePrereleaseOnRelease = enablePrereleaseOnRelease;
        return definition;
    }

    /// <summary>Sets whether JSON file versioning is used.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="useJsonFileVersioning">True to use JSON versioning.</param>
    /// <returns>The definition.</returns>
    public static TDefinition UseJsonFileVersioning<TDefinition>(this TDefinition definition, bool useJsonFileVersioning)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.UseJsonFileVersioning = () => Task.FromResult(useJsonFileVersioning);
        return definition;
    }

    /// <summary>Sets JSON file versioning from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="useJsonFileVersioning">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition UseJsonFileVersioning<TDefinition>(this TDefinition definition, Func<bool> useJsonFileVersioning)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(useJsonFileVersioning);
        definition.UseJsonFileVersioning = () => Task.FromResult(useJsonFileVersioning());
        return definition;
    }

    /// <summary>Sets JSON file versioning from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="useJsonFileVersioning">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition UseJsonFileVersioning<TDefinition>(this TDefinition definition, Func<Task<bool>> useJsonFileVersioning)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(useJsonFileVersioning);
        definition.UseJsonFileVersioning = useJsonFileVersioning;
        return definition;
    }
}
