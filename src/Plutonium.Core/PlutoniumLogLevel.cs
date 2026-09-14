namespace Plutonium.Core;

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
