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
/// Pipeline the run executes on. Helpers-own; mapped from <see cref="PlutoniumHostKind"/>.
/// </summary>
public enum PlutoniumPipelineType
{
    /// <summary>Azure Pipelines.</summary>
    Azure = 0,

    /// <summary>GitHub Actions.</summary>
    GitHub = 1,

    /// <summary>Local developer machine; no CI host detected.</summary>
    Local = 2,
}

/// <summary>
/// Maps the Core host detection onto the Helpers pipeline type.
/// </summary>
public static class PlutoniumPipelineTypeExtensions
{
    /// <summary>
    /// Maps a detected Core host kind onto the Helpers pipeline type.
    /// </summary>
    /// <param name="kind">The detected host kind.</param>
    /// <returns>The pipeline type. Never null.</returns>
    public static PlutoniumPipelineType ToPipelineType(this PlutoniumHostKind kind) => kind switch
    {
        PlutoniumHostKind.GitHubActions => PlutoniumPipelineType.GitHub,
        PlutoniumHostKind.AzurePipelines => PlutoniumPipelineType.Azure,
        _ => PlutoniumPipelineType.Local,
    };
}
