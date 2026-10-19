using System.Reflection;
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
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Entries.Discovery;
/// <summary>
/// Optional base for build authors: exposes the <c>WorkflowConfig</c> property
/// discovery looks for plus entry delegates. Authors may also declare entries
/// on any plain object without inheriting this base.
/// </summary>
public abstract class PlutoniumBuildBase
{
    /// <summary>
    /// Gets the workflow configuration delegate. Override to configure the pipeline.
    /// </summary>
    protected virtual WorkflowConfigEntry WorkflowConfig => definition => definition;
}
