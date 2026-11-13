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
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Pipelines.Context;
/// <summary>
/// Contract every pipeline (GitHub, Azure, Local) implements: info detection,
/// pre-setup prepare/finalize, per-entry run prepare/finalize, post-setup
/// prepare/finalize, and workflow generation.
/// </summary>
public interface IPlutoniumPipeline
{
    /// <summary>Gets the pipeline type.</summary>
    PlutoniumPipelineType PipelineType { get; }

    /// <summary>
    /// Detects the pipeline info: branch, trigger, and pull-request number.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pipeline info. Never null.</returns>
    Task<PlutoniumPipelineInfo> GetPipelineInfoAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the pre-setup payload transported from the pre-setup job.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payload. Never null.</returns>
    Task<PlutoniumPipelinePreSetup> GetPipelinePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs pre-setup steps before the payload is computed (checkout setup, secret export).
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the preparation.</returns>
    Task PreparePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the computed payload to the pipeline environment.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="preSetup">The computed payload. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the finalization.</returns>
    Task FinalizePreSetupAsync(PlutoniumPipelineContext context, PlutoniumPipelinePreSetup preSetup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets up run contexts for one entry before it executes.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="entry">The entry. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the preparation.</returns>
    Task PrepareEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Packs artifacts after one entry executes.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="entry">The entry. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the finalization.</returns>
    Task FinalizeEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares post-setup (artifact unpack, release context load).
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the preparation.</returns>
    Task PreparePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes releases and stamps version markers after all entries finish.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the finalization.</returns>
    Task FinalizePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the workflow document plus the opaque emitter blocks for this pipeline.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document and emitter blocks. Never null.</returns>
    Task<PlutoniumWorkflowBuildResult> BuildWorkflowAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);
}
