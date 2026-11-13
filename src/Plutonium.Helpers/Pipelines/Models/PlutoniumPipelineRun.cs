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
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Pipelines.Models;

/// <summary>
/// Selected pipeline plus its info and type for one run.
/// </summary>
public sealed class PlutoniumPipelineRun
{
    /// <summary>Gets the pipeline. Never null.</summary>
    public IPlutoniumPipeline Pipeline { get; }

    /// <summary>Gets the pipeline info. Never null.</summary>
    public PlutoniumPipelineInfo Info { get; }

    /// <summary>Gets the pipeline type.</summary>
    public PlutoniumPipelineType PipelineType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumPipelineRun"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline. Must not be null.</param>
    /// <param name="info">The pipeline info. Must not be null.</param>
    /// <param name="pipelineType">The pipeline type.</param>
    public PlutoniumPipelineRun(IPlutoniumPipeline pipeline, PlutoniumPipelineInfo info, PlutoniumPipelineType pipelineType)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(info);
        Pipeline = pipeline;
        Info = info;
        PipelineType = pipelineType;
    }
}
