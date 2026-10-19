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
/// Kind of run an entry executes for.
/// </summary>
[Flags]
public enum PlutoniumRunType
{
    /// <summary>No run type selected.</summary>
    None = 0,

    /// <summary>Local run only, excluding pipeline execution.</summary>
    Local = 1,

    /// <summary>Run triggered by a pull request.</summary>
    PullRequest = 2,

    /// <summary>Run triggered by a commit on environment branches, excluding pull requests.</summary>
    Commit = 4,

    /// <summary>Run triggered by a version bump.</summary>
    Bump = 8,

    /// <summary>Run for the target app entry only if it is also running.</summary>
    Target = 16,
}
