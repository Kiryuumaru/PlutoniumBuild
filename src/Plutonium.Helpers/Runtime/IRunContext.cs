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
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Runtime;
/// <summary>
/// Per-run execution context handed to every <c>Execute</c>, <c>Condition</c>,
/// and <c>RunnerOS</c> lambda. Multi-app runs see all apps in <see cref="Apps"/>.
/// </summary>
public interface IRunContext
{
    /// <summary>Gets the pipeline kind for this run.</summary>
    PlutoniumPipelineType PipelineType { get; }

    /// <summary>Gets the per-app contexts keyed by AppId.</summary>
    IReadOnlyDictionary<PlutoniumAppId, AppRunContext> Apps { get; }

    /// <summary>Gets the terminal surface for running external tools. Never null.</summary>
    PlutoniumTerminal Terminal { get; }
}
