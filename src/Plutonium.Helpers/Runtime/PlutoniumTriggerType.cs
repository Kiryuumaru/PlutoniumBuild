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
/// What triggered the run. Consumed by future workflow emitters; local runs report Local.
/// </summary>
public enum PlutoniumTriggerType
{
    /// <summary>Triggered by a pull request.</summary>
    PullRequest = 0,

    /// <summary>Triggered by a tag push.</summary>
    Tag = 1,

    /// <summary>Triggered by a branch commit.</summary>
    Commit = 2,

    /// <summary>Local developer run.</summary>
    Local = 3,
}
