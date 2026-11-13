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
