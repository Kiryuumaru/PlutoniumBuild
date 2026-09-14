namespace Plutonium.Core;

/// <summary>
/// Frozen sink contract for Plutonium log output.
/// Implementations receive fully formatted, secret-masked messages.
/// </summary>
public interface IPlutoniumLogSink
{
    /// <summary>
    /// Writes a single log entry to the sink.
    /// </summary>
    /// <param name="level">The severity of the entry.</param>
    /// <param name="scope">The active scope name, or null when outside any target scope.</param>
    /// <param name="message">The masked message text. Never null.</param>
    void Write(PlutoniumLogLevel level, string? scope, string message);
}
