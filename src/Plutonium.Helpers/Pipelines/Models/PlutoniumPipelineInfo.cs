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
/// Detected pipeline info: the branch, what triggered the run, and the pull-request number.
/// Parsed from CI refs with a git-branch fallback; local runs report Local.
/// </summary>
public sealed class PlutoniumPipelineInfo
{
    /// <summary>Gets the branch name. Never null or whitespace.</summary>
    public string Branch { get; }

    /// <summary>Gets what triggered the run.</summary>
    public PlutoniumTriggerType TriggerType { get; }

    /// <summary>Gets the pull-request number. Zero unless the trigger is a pull request.</summary>
    public long PullRequestNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumPipelineInfo"/> class.
    /// </summary>
    /// <param name="branch">The branch name. Must not be null or whitespace.</param>
    /// <param name="triggerType">What triggered the run.</param>
    /// <param name="pullRequestNumber">The pull-request number. Zero unless pull request.</param>
    public PlutoniumPipelineInfo(string branch, PlutoniumTriggerType triggerType, long pullRequestNumber = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        Branch = branch.Trim();
        TriggerType = triggerType;
        PullRequestNumber = pullRequestNumber;
    }
}
