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
/// Pipeline context: the build instance, its options and services, pipeline type,
/// and shared per-run state. One context flows through local and pipeline runs.
/// </summary>
public sealed class PlutoniumPipelineContext
{
    /// <summary>Gets the build instance holding entry delegates. Never null.</summary>
    public object Build { get; }

    /// <summary>Gets the build options. Never null.</summary>
    public PlutoniumBuildOptions Options { get; }

    /// <summary>Gets the build services. Never null.</summary>
    public PlutoniumBuildServices Services { get; }

    /// <summary>Gets the pipeline type for this run.</summary>
    public PlutoniumPipelineType PipelineType { get; set; }

    /// <summary>Gets the discovered entries, set by setup. May be null before discovery.</summary>
    public PlutoniumAllEntries? Entries { get; set; }

    /// <summary>Gets or sets the pre-setup payload once computed.</summary>
    public PlutoniumPipelinePreSetup? PreSetup { get; set; }

    /// <summary>Gets the split <c>--args</c> map: AppId to bump expression. Never null.</summary>
    public IReadOnlyDictionary<string, string?> SplitArgs { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumPipelineContext"/> class.
    /// </summary>
    /// <param name="build">The build instance. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="services">The build services. Must not be null.</param>
    /// <param name="pipelineType">The pipeline type.</param>
    public PlutoniumPipelineContext(
        object build,
        PlutoniumBuildOptions options,
        PlutoniumBuildServices services,
        PlutoniumPipelineType pipelineType)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        Build = build;
        Options = options;
        Services = services;
        PipelineType = pipelineType;
    }

    /// <summary>Gets the Core logger shortcut.</summary>
    public IPlutoniumLogger Logger => Services.Logger;
}
