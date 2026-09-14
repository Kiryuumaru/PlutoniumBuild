using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Base run-entry definition carrying the documented defaults: Id <c>_</c>,
/// empty AppIds, always-true condition, unset RunnerOS, empty cache paths,
/// invalidator <c>0</c>, fetch depth 1, no tag fetch, no submodules, and
/// empty execute, workflow-builder, and matrix lists.
/// </summary>
public abstract class RunEntryDefinition : IRunEntryDefinition
{
    private string? _id;
    private List<PlutoniumAppId> _appIds = [];
    private Func<IPlutoniumWorkflowBuilder, Task<string>>? _displayName;
    private Func<IRunContext, Task<bool>>? _condition;
    private List<Func<IRunContext, Task<PlutoniumPath[]>>>? _cachePath;
    private Func<IRunContext, Task<string>>? _cacheInvalidator;
    private Func<IRunContext, Task<int>>? _checkoutFetchDepth;
    private Func<IRunContext, Task<bool>>? _checkoutFetchTags;
    private Func<IRunContext, Task<PlutoniumSubmoduleCheckoutType>>? _checkoutSubmodules;
    private List<Func<IRunContext, Task>>? _execute;
    private List<Func<IPlutoniumWorkflowBuilder, Task>>? _workflowBuilder;
    private List<Func<IRunEntryDefinition, Task<IRunEntryDefinition[]>>>? _matrix;

    /// <inheritdoc/>
    public string Id
    {
        get => _id ?? "_";
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _id = value.Trim();
        }
    }

    /// <inheritdoc/>
    public List<PlutoniumAppId> AppIds
    {
        get => _appIds;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _appIds = value;
        }
    }

    /// <inheritdoc/>
    Func<IPlutoniumWorkflowBuilder, Task<string>> IRunEntryDefinition.DisplayName
    {
        get => _displayName ?? (_ => Task.FromResult(GetDefaultName()));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _displayName = value;
        }
    }

    /// <inheritdoc/>
    Func<IRunContext, Task<bool>> IRunEntryDefinition.Condition
    {
        get => _condition ?? (_ => Task.FromResult(true));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _condition = value;
        }
    }

    /// <inheritdoc/>
    Func<IRunContext, Task<PlutoniumRunnerOS>>? IRunEntryDefinition.RunnerOS { get; set; }

    /// <inheritdoc/>
    List<Func<IRunContext, Task<PlutoniumPath[]>>> IRunEntryDefinition.CachePath
    {
        get => _cachePath ??= [];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _cachePath = value;
        }
    }

    /// <inheritdoc/>
    Func<IRunContext, Task<string>> IRunEntryDefinition.CacheInvalidator
    {
        get => _cacheInvalidator ?? (_ => Task.FromResult("0"));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _cacheInvalidator = value;
        }
    }

    /// <inheritdoc/>
    Func<IRunContext, Task<int>> IRunEntryDefinition.CheckoutFetchDepth
    {
        get => _checkoutFetchDepth ?? (_ => Task.FromResult(1));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _checkoutFetchDepth = value;
        }
    }

    /// <inheritdoc/>
    Func<IRunContext, Task<bool>> IRunEntryDefinition.CheckoutFetchTags
    {
        get => _checkoutFetchTags ?? (_ => Task.FromResult(false));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _checkoutFetchTags = value;
        }
    }

    /// <inheritdoc/>
    Func<IRunContext, Task<PlutoniumSubmoduleCheckoutType>> IRunEntryDefinition.CheckoutSubmodules
    {
        get => _checkoutSubmodules ?? (_ => Task.FromResult(PlutoniumSubmoduleCheckoutType.None));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _checkoutSubmodules = value;
        }
    }

    /// <inheritdoc/>
    List<Func<IRunContext, Task>> IRunEntryDefinition.Execute
    {
        get => _execute ??= [];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _execute = value;
        }
    }

    /// <inheritdoc/>
    List<Func<IPlutoniumWorkflowBuilder, Task>> IRunEntryDefinition.WorkflowBuilder
    {
        get => _workflowBuilder ??= [];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _workflowBuilder = value;
        }
    }

    /// <inheritdoc/>
    List<Func<IRunEntryDefinition, Task<IRunEntryDefinition[]>>> IRunEntryDefinition.Matrix
    {
        get => _matrix ??= [];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _matrix = value;
        }
    }

    /// <inheritdoc/>
    IRunContext? IRunEntryDefinition.RunContext { get; set; }

    /// <inheritdoc/>
    public abstract IRunEntryDefinition Clone();

    /// <inheritdoc/>
    public async Task<string> GetDisplayNameAsync(IPlutoniumWorkflowBuilder workflowBuilder)
    {
        ArgumentNullException.ThrowIfNull(workflowBuilder);
        var self = (IRunEntryDefinition)this;
        var name = await self.DisplayName(workflowBuilder).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    /// <inheritdoc/>
    public Task<bool> GetConditionAsync()
    {
        var context = RequireContext();
        return ((IRunEntryDefinition)this).Condition(context);
    }

    /// <inheritdoc/>
    public Task<PlutoniumRunnerOS?> GetRunnerOSAsync()
    {
        var selector = ((IRunEntryDefinition)this).RunnerOS;
        if (selector is null)
        {
            return Task.FromResult<PlutoniumRunnerOS?>(null);
        }

        return ResolveRunnerOSAsync(selector);
    }

    /// <inheritdoc/>
    public async Task<PlutoniumPath[]> GetCachePathsAsync()
    {
        var context = RequireContext();
        var self = (IRunEntryDefinition)this;
        var collected = new List<PlutoniumPath>();
        foreach (var provider in self.CachePath)
        {
            var paths = await provider(context).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(paths);
            collected.AddRange(paths);
        }

        return [.. collected];
    }

    /// <inheritdoc/>
    public async Task<string> GetCacheInvalidatorAsync()
    {
        var self = (IRunEntryDefinition)this;
        var invalidator = await self.CacheInvalidator(RequireContext()).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidator);
        return invalidator;
    }

    /// <inheritdoc/>
    public Task<int> GetCheckoutFetchDepthAsync() => ((IRunEntryDefinition)this).CheckoutFetchDepth(RequireContext());

    /// <inheritdoc/>
    public Task<bool> GetCheckoutFetchTagsAsync() => ((IRunEntryDefinition)this).CheckoutFetchTags(RequireContext());

    /// <inheritdoc/>
    public Task<PlutoniumSubmoduleCheckoutType> GetCheckoutSubmodulesAsync() =>
        ((IRunEntryDefinition)this).CheckoutSubmodules(RequireContext());

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        var self = (IRunEntryDefinition)this;
        var context = RequireContext();
        foreach (var step in self.Execute)
        {
            await step(context).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task BuildWorkflowAsync(IPlutoniumWorkflowBuilder workflowBuilder)
    {
        ArgumentNullException.ThrowIfNull(workflowBuilder);
        foreach (var step in ((IRunEntryDefinition)this).WorkflowBuilder)
        {
            await step(workflowBuilder).ConfigureAwait(false);
        }
    }

    /// <summary>Copies every configured knob onto the target definition.</summary>
    /// <param name="target">The target. Must not be null.</param>
    protected void FillClone(IRunEntryDefinition target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var self = (IRunEntryDefinition)this;
        if (_id is not null)
        {
            target.Id = _id;
        }

        target.AppIds = [.. _appIds];
        if (_displayName is not null)
        {
            target.DisplayName = _displayName;
        }

        if (_condition is not null)
        {
            target.Condition = _condition;
        }

        target.RunnerOS = self.RunnerOS;
        if (_cachePath is not null)
        {
            target.CachePath = [.. _cachePath];
        }

        if (_cacheInvalidator is not null)
        {
            target.CacheInvalidator = _cacheInvalidator;
        }

        if (_checkoutFetchDepth is not null)
        {
            target.CheckoutFetchDepth = _checkoutFetchDepth;
        }

        if (_checkoutFetchTags is not null)
        {
            target.CheckoutFetchTags = _checkoutFetchTags;
        }

        if (_checkoutSubmodules is not null)
        {
            target.CheckoutSubmodules = _checkoutSubmodules;
        }

        if (_execute is not null)
        {
            target.Execute = [.. _execute];
        }

        if (_workflowBuilder is not null)
        {
            target.WorkflowBuilder = [.. _workflowBuilder];
        }

        if (_matrix is not null)
        {
            target.Matrix = [.. _matrix];
        }

        target.RunContext = self.RunContext;
    }

    /// <summary>Computes the default display name from the id and AppIds.</summary>
    /// <returns>The default name. Never null.</returns>
    protected abstract string GetDefaultName();

    /// <summary>Builds the entry-kind prefix used by default display names.</summary>
    /// <param name="kind">The kind prefix (for example <c>Build</c>).</param>
    /// <returns>The default name. Never null.</returns>
    protected string DefaultNameFor(string kind)
    {
        var self = (IRunEntryDefinition)this;
        if (self.AppIds.Count == 1)
        {
            return $"{kind} - {self.AppIds[0]} ({self.Id})";
        }

        return $"{kind} - {self.Id}";
    }

    private IRunContext RequireContext() =>
        ((IRunEntryDefinition)this).RunContext ?? throw new InvalidOperationException($"Entry '{((IRunEntryDefinition)this).Id}' has no run context.");

    private async Task<PlutoniumRunnerOS?> ResolveRunnerOSAsync(Func<IRunContext, Task<PlutoniumRunnerOS>> selector)
    {
        var os = await selector(RequireContext()).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(os);
        return os;
    }
}

/// <summary>Base for dependent entries (test).</summary>
public abstract class DependentEntryDefinition : RunEntryDefinition, IDependentEntryDefinition
{
}

/// <summary>Base for target-backed entries (build and publish).</summary>
public abstract class TargetEntryDefinition : RunEntryDefinition, ITargetEntryDefinition
{
}

/// <summary>Build entry definition.</summary>
public sealed class BuildEntryDefinition : TargetEntryDefinition, IBuildEntryDefinition
{
    /// <inheritdoc/>
    public override IRunEntryDefinition Clone()
    {
        var clone = new BuildEntryDefinition();
        FillClone(clone);
        return clone;
    }

    /// <inheritdoc/>
    protected override string GetDefaultName() => DefaultNameFor("Build");
}

/// <summary>Test entry definition.</summary>
public sealed class TestEntryDefinition : DependentEntryDefinition, ITestEntryDefinition
{
    private Func<Task<bool>>? _executeBeforeBuild;

    /// <inheritdoc/>
    Func<Task<bool>> ITestEntryDefinition.ExecuteBeforeBuild
    {
        get => _executeBeforeBuild ?? (() => Task.FromResult(false));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _executeBeforeBuild = value;
        }
    }

    /// <inheritdoc/>
    public Task<bool> GetExecuteBeforeBuildAsync() => ((ITestEntryDefinition)this).ExecuteBeforeBuild();

    /// <inheritdoc/>
    public override IRunEntryDefinition Clone()
    {
        var clone = new TestEntryDefinition();
        FillClone(clone);
        if (_executeBeforeBuild is not null)
        {
            ((ITestEntryDefinition)clone).ExecuteBeforeBuild = _executeBeforeBuild;
        }

        return clone;
    }

    /// <inheritdoc/>
    protected override string GetDefaultName() => DefaultNameFor("Test");
}

/// <summary>Publish entry definition.</summary>
public sealed class PublishEntryDefinition : TargetEntryDefinition, IPublishEntryDefinition
{
    /// <inheritdoc/>
    public override IRunEntryDefinition Clone()
    {
        var clone = new PublishEntryDefinition();
        FillClone(clone);
        return clone;
    }

    /// <inheritdoc/>
    protected override string GetDefaultName() => DefaultNameFor("Publish");
}

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
