using System.Text.Json;
using System.Text.Json.Serialization;
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
/// RunnerOS setup captured for one entry: display name, opaque pipeline payload, and run script.
/// </summary>
public sealed class PlutoniumRunnerSetup
{
    /// <summary>Gets the runner name (for example <c>ubuntu-22.04</c>). Never null.</summary>
    public string Name { get; }

    /// <summary>Gets the opaque pipeline OS payload as JSON. Never null.</summary>
    public string RunnerPipelineOS { get; }

    /// <summary>Gets the run script invoking the build. Never null.</summary>
    public string RunScript { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumRunnerSetup"/> class.
    /// </summary>
    /// <param name="name">The runner name. Must not be null or whitespace.</param>
    /// <param name="runnerPipelineOS">The opaque pipeline OS payload. Must not be null.</param>
    /// <param name="runScript">The run script. Must not be null or whitespace.</param>
    public PlutoniumRunnerSetup(string name, string runnerPipelineOS, string runScript)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(runnerPipelineOS);
        ArgumentException.ThrowIfNullOrWhiteSpace(runScript);
        Name = name.Trim();
        RunnerPipelineOS = runnerPipelineOS;
        RunScript = runScript.Trim();
    }
}
