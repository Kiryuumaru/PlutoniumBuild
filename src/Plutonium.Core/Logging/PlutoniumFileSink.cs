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
/// <see cref="IPlutoniumLogSink"/> appending level-prefixed lines to a text file.
/// BCL-only; thread-safe for concurrent runner threads.
/// </summary>
public sealed class PlutoniumFileSink : IPlutoniumLogSink, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Gets the log file path. Never null.</summary>
    public PlutoniumPath Path { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumFileSink"/> class.
    /// Creates parent directories as needed.
    /// </summary>
    /// <param name="path">The log file path. Must not be null.</param>
    /// <param name="append">Whether to append to an existing file; otherwise it is truncated.</param>
    public PlutoniumFileSink(PlutoniumPath path, bool append = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path.Value));
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(path.Value, append) { AutoFlush = true };
    }

    /// <inheritdoc/>
    public void Write(PlutoniumLogLevel level, string? scope, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PlutoniumFileSink));
        }

        var line = scope is null
            ? $"{level}: {message}"
            : $"{level}: [{scope}] {message}";
        lock (_gate)
        {
            _writer.WriteLine(line);
        }
    }

    /// <summary>
    /// Flushes and closes the log file.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }
}
