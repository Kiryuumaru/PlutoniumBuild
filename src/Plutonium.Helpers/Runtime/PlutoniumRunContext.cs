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
/// Default <see cref="IRunContext"/> implementation used for local and pipeline runs.
/// </summary>
public sealed class PlutoniumRunContext : IRunContext
{
    /// <summary>Gets the pipeline kind for this run.</summary>
    public PlutoniumPipelineType PipelineType { get; }

    /// <summary>Gets the per-app contexts keyed by AppId.</summary>
    public IReadOnlyDictionary<PlutoniumAppId, AppRunContext> Apps { get; }

    /// <summary>Gets the terminal surface for running external tools. Never null.</summary>
    public PlutoniumTerminal Terminal { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumRunContext"/> class.
    /// </summary>
    /// <param name="pipelineType">The pipeline kind.</param>
    /// <param name="apps">The per-app contexts. Must not be null; keys must match each context's AppId.</param>
    /// <param name="terminal">The terminal surface. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="apps"/> or <paramref name="terminal"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a key does not match its context's AppId.</exception>
    public PlutoniumRunContext(
        PlutoniumPipelineType pipelineType,
        IReadOnlyDictionary<PlutoniumAppId, AppRunContext> apps,
        PlutoniumTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(terminal);
        foreach (var pair in apps)
        {
            ArgumentNullException.ThrowIfNull(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!pair.Key.Equals(pair.Value.AppId))
            {
                throw new ArgumentException(
                    $"Apps key '{pair.Key}' does not match context AppId '{pair.Value.AppId}'.",
                    nameof(apps));
            }
        }

        PipelineType = pipelineType;
        Apps = apps;
        Terminal = terminal;
    }
}
