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
/// Redacts registered secret values from text before it reaches a log sink.
/// Longer secrets are replaced first so overlapping values mask correctly.
/// </summary>
public sealed class PlutoniumSecretMasker
{
    private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Registers a secret value for masking. Null or empty values are ignored.</summary>
    /// <param name="secret">The secret value to mask.</param>
    public void Register(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        lock (_gate)
        {
            _secrets.Add(secret);
        }
    }

    /// <summary>Masks all registered secrets in the given text.</summary>
    /// <param name="text">The text to mask. Must not be null.</param>
    /// <returns>The text with every secret occurrence replaced by <c>***</c>.</returns>
    public string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _secrets.OrderByDescending(s => s.Length)];
        }

        foreach (var secret in snapshot)
        {
            text = text.Replace(secret, "***", StringComparison.Ordinal);
        }

        return text;
    }
}

/// <summary>
/// Cross-layer logging abstraction: level switch, per-target scope, secret masking.
/// Thread-safe; usable from any runner thread.
/// </summary>
/// <remarks>
/// Callers pre-mask by registering every secret value via
/// <see cref="IPlutoniumLogger.RegisterSecret"/> before logging text that may contain it.
/// Messages reaching sinks are already masked.
/// </remarks>
public interface IPlutoniumLogger
{
    /// <summary>Gets or sets the minimum level that is written to sinks.</summary>
    PlutoniumLogLevel MinimumLevel { get; set; }

    /// <summary>Gets the active scope name, or null when outside any target scope.</summary>
    string? CurrentScope { get; }

    /// <summary>Registers a secret value so it never appears in sink output.</summary>
    /// <param name="secret">The secret value to mask. Register before logging any text containing it.</param>
    void RegisterSecret(string? secret);

    /// <summary>Writes a message at the given level when it passes the level switch.</summary>
    /// <param name="level">The severity of the message.</param>
    /// <param name="message">The message text. Must not be null.</param>
    void Log(PlutoniumLogLevel level, string message);

    /// <summary>Opens a named scope; the previous scope is restored on dispose.</summary>
    /// <param name="scope">The scope name. Must not be null.</param>
    /// <returns>A disposable that restores the previous scope.</returns>
    IDisposable BeginScope(string scope);
}

/// <summary>
/// Default <see cref="IPlutoniumLogger"/> fanning out to one or more sinks.
/// Applies the level switch and secret masking before dispatch.
/// </summary>
public sealed class PlutoniumLogger : IPlutoniumLogger
{
    private readonly IReadOnlyList<IPlutoniumLogSink> _sinks;
    private readonly PlutoniumSecretMasker _masker = new();
    private readonly AsyncLocal<string?> _scope = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumLogger"/> class.
    /// </summary>
    /// <param name="sinks">At least one sink. Must not be null or empty.</param>
    /// <param name="minimumLevel">The initial minimum level.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sinks"/> is empty.</exception>
    public PlutoniumLogger(IEnumerable<IPlutoniumLogSink> sinks, PlutoniumLogLevel minimumLevel = PlutoniumLogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        var materialized = sinks.ToList();
        if (materialized.Count == 0)
        {
            throw new ArgumentException("At least one sink is required.", nameof(sinks));
        }

        _sinks = materialized;
        MinimumLevel = minimumLevel;
    }

    /// <inheritdoc/>
    public PlutoniumLogLevel MinimumLevel { get; set; }

    /// <inheritdoc/>
    public string? CurrentScope => _scope.Value;

    /// <inheritdoc/>
    public void RegisterSecret(string? secret) => _masker.Register(secret);

    /// <inheritdoc/>
    public void Log(PlutoniumLogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (level < MinimumLevel)
        {
            return;
        }

        var masked = _masker.Mask(message);
        foreach (var sink in _sinks)
        {
            sink.Write(level, _scope.Value, masked);
        }
    }

    /// <inheritdoc/>
    public IDisposable BeginScope(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var previous = _scope.Value;
        _scope.Value = scope;
        return new ScopeGuard(this, previous);
    }

    private sealed class ScopeGuard(PlutoniumLogger owner, string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner._scope.Value = previous;
            }
        }
    }
}

/// <summary>
/// <see cref="IPlutoniumLogSink"/> writing level-prefixed lines to the console.
/// </summary>
public sealed class PlutoniumConsoleSink : IPlutoniumLogSink
{
    private readonly TextWriter _output;
    private readonly TextWriter _errors;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumConsoleSink"/> class.
    /// </summary>
    /// <param name="output">Writer for non-error levels. Defaults to <see cref="Console.Out"/>.</param>
    /// <param name="errors">Writer for error levels. Defaults to <see cref="Console.Error"/>.</param>
    public PlutoniumConsoleSink(TextWriter? output = null, TextWriter? errors = null)
    {
        _output = output ?? Console.Out;
        _errors = errors ?? Console.Error;
    }

    /// <inheritdoc/>
    public void Write(PlutoniumLogLevel level, string? scope, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var prefix = level switch
        {
            PlutoniumLogLevel.Trace => "trce",
            PlutoniumLogLevel.Debug => "dbug",
            PlutoniumLogLevel.Information => "info",
            PlutoniumLogLevel.Warning => "warn",
            PlutoniumLogLevel.Error => "fail",
            _ => "info",
        };

        var line = scope is null
            ? $"{prefix}: {message}"
            : $"{prefix}: [{scope}] {message}";

        (level >= PlutoniumLogLevel.Error ? _errors : _output).WriteLine(line);
    }
}
