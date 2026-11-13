using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Logging;

/// <summary>
/// Severity levels for Plutonium logging, in ascending order.
/// </summary>
public enum PlutoniumLogLevel
{
    /// <summary>Fine-grained diagnostic messages.</summary>
    Trace = 0,

    /// <summary>Diagnostic messages useful during development.</summary>
    Debug = 1,

    /// <summary>General informational messages.</summary>
    Information = 2,

    /// <summary>Unexpected but recoverable situations.</summary>
    Warning = 3,

    /// <summary>Failures that prevent an operation from completing.</summary>
    Error = 4,
}
