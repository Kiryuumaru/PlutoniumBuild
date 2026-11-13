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
