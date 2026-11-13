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
/// Workflow-config definition with the six documented defaults: name
/// <c>Plutonium CICD Pipeline</c>, pre/post setup OSes Ubuntu2204, asset hashes
/// true, prerelease on release true, JSON file versioning false.
/// </summary>
public sealed class WorkflowConfigEntryDefinition : IWorkflowConfigEntryDefinition
{
    private Func<Task<string>>? _name;
    private Func<Task<PlutoniumRunnerOS>>? _preSetupRunnerOS;
    private Func<Task<PlutoniumRunnerOS>>? _postSetupRunnerOS;
    private Func<Task<bool>>? _appendReleaseNotesAssetHashes;
    private Func<Task<bool>>? _enablePrereleaseOnRelease;
    private Func<Task<bool>>? _useJsonFileVersioning;

    /// <inheritdoc/>
    Func<Task<string>> IWorkflowConfigEntryDefinition.Name
    {
        get => _name ?? (() => Task.FromResult("Plutonium CICD Pipeline"));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _name = value;
        }
    }

    /// <inheritdoc/>
    Func<Task<PlutoniumRunnerOS>> IWorkflowConfigEntryDefinition.PreSetupRunnerOS
    {
        get => _preSetupRunnerOS ?? (() => Task.FromResult(PlutoniumRunnerOS.Ubuntu2204));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _preSetupRunnerOS = value;
        }
    }

    /// <inheritdoc/>
    Func<Task<PlutoniumRunnerOS>> IWorkflowConfigEntryDefinition.PostSetupRunnerOS
    {
        get => _postSetupRunnerOS ?? (() => Task.FromResult(PlutoniumRunnerOS.Ubuntu2204));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _postSetupRunnerOS = value;
        }
    }

    /// <inheritdoc/>
    Func<Task<bool>> IWorkflowConfigEntryDefinition.AppendReleaseNotesAssetHashes
    {
        get => _appendReleaseNotesAssetHashes ?? (() => Task.FromResult(true));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _appendReleaseNotesAssetHashes = value;
        }
    }

    /// <inheritdoc/>
    Func<Task<bool>> IWorkflowConfigEntryDefinition.EnablePrereleaseOnRelease
    {
        get => _enablePrereleaseOnRelease ?? (() => Task.FromResult(true));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _enablePrereleaseOnRelease = value;
        }
    }

    /// <inheritdoc/>
    Func<Task<bool>> IWorkflowConfigEntryDefinition.UseJsonFileVersioning
    {
        get => _useJsonFileVersioning ?? (() => Task.FromResult(false));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _useJsonFileVersioning = value;
        }
    }

    /// <inheritdoc/>
    public IWorkflowConfigEntryDefinition Clone()
    {
        var clone = new WorkflowConfigEntryDefinition();
        var target = (IWorkflowConfigEntryDefinition)clone;
        if (_name is not null)
        {
            target.Name = _name;
        }

        if (_preSetupRunnerOS is not null)
        {
            target.PreSetupRunnerOS = _preSetupRunnerOS;
        }

        if (_postSetupRunnerOS is not null)
        {
            target.PostSetupRunnerOS = _postSetupRunnerOS;
        }

        if (_appendReleaseNotesAssetHashes is not null)
        {
            target.AppendReleaseNotesAssetHashes = _appendReleaseNotesAssetHashes;
        }

        if (_enablePrereleaseOnRelease is not null)
        {
            target.EnablePrereleaseOnRelease = _enablePrereleaseOnRelease;
        }

        if (_useJsonFileVersioning is not null)
        {
            target.UseJsonFileVersioning = _useJsonFileVersioning;
        }

        return clone;
    }

    /// <inheritdoc/>
    public async Task<string> GetNameAsync()
    {
        var name = await ((IWorkflowConfigEntryDefinition)this).Name().ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    /// <inheritdoc/>
    public async Task<PlutoniumRunnerOS> GetPreSetupRunnerOSAsync()
    {
        var os = await ((IWorkflowConfigEntryDefinition)this).PreSetupRunnerOS().ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(os);
        return os;
    }

    /// <inheritdoc/>
    public async Task<PlutoniumRunnerOS> GetPostSetupRunnerOSAsync()
    {
        var os = await ((IWorkflowConfigEntryDefinition)this).PostSetupRunnerOS().ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(os);
        return os;
    }

    /// <inheritdoc/>
    public Task<bool> GetAppendReleaseNotesAssetHashesAsync() => ((IWorkflowConfigEntryDefinition)this).AppendReleaseNotesAssetHashes();

    /// <inheritdoc/>
    public Task<bool> GetEnablePrereleaseOnReleaseAsync() => ((IWorkflowConfigEntryDefinition)this).EnablePrereleaseOnRelease();

    /// <inheritdoc/>
    public Task<bool> GetUseJsonFileVersioningAsync() => ((IWorkflowConfigEntryDefinition)this).UseJsonFileVersioning();
}
