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
/// Workflow build result: the Core document plus the opaque emitter blocks.
/// </summary>
public sealed class PlutoniumWorkflowBuildResult
{
    /// <summary>Gets the workflow document. Never null.</summary>
    public PlutoniumWorkflowDocument Document { get; }

    /// <summary>Gets the first opaque emitter block (concurrency or trigger). May be null.</summary>
    public string? FirstBlock { get; }

    /// <summary>Gets the second opaque emitter block (permissions or pr). May be null.</summary>
    public string? SecondBlock { get; }

    /// <summary>Gets the third opaque emitter block (pool). May be null.</summary>
    public string? ThirdBlock { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumWorkflowBuildResult"/> class.
    /// </summary>
    /// <param name="document">The workflow document. Must not be null.</param>
    /// <param name="firstBlock">The first emitter block. May be null.</param>
    /// <param name="secondBlock">The second emitter block. May be null.</param>
    /// <param name="thirdBlock">The third emitter block. May be null.</param>
    public PlutoniumWorkflowBuildResult(
        PlutoniumWorkflowDocument document,
        string? firstBlock = null,
        string? secondBlock = null,
        string? thirdBlock = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        FirstBlock = firstBlock;
        SecondBlock = secondBlock;
        ThirdBlock = thirdBlock;
    }
}
