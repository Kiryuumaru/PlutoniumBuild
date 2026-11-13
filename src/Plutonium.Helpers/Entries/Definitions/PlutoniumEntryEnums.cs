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
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Entries.Definitions;

/// <summary>
/// Kind of run entry. Drives per-app grouping and workflow fan-out.
/// </summary>
public enum PlutoniumEntryType
{
    /// <summary>Test entry.</summary>
    Test = 0,

    /// <summary>Build entry.</summary>
    Build = 1,

    /// <summary>Publish entry.</summary>
    Publish = 2,
}

/// <summary>
/// How pipeline checkout handles git submodules for an entry.
/// </summary>
public enum PlutoniumSubmoduleCheckoutType
{
    /// <summary>Do not fetch submodules.</summary>
    None = 0,

    /// <summary>Checkout a single level of submodules.</summary>
    SingleLevel = 1,

    /// <summary>Checkout submodules of submodules.</summary>
    Recursive = 2,
}
