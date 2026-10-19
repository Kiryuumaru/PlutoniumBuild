using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.FileSystem;

/// <summary>
/// Frozen path value type used across Core and Helpers.
/// Wraps an absolute or relative filesystem path with lexical normalization only.
/// No I/O is performed on construction.
/// </summary>
public sealed class PlutoniumPath : IEquatable<PlutoniumPath>
{
    /// <summary>Gets the underlying path string as provided, trimmed of surrounding whitespace.</summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumPath"/> class.
    /// </summary>
    /// <param name="value">The path string. Must not be null, empty, or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    public PlutoniumPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    /// <summary>Combines this path with additional segments.</summary>
    /// <param name="segments">Path segments to append. Each must not be null.</param>
    /// <returns>A new <see cref="PlutoniumPath"/> representing the combined path.</returns>
    public PlutoniumPath Combine(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        foreach (var segment in segments)
        {
            ArgumentNullException.ThrowIfNull(segment, nameof(segments));
        }

        var parts = new string[segments.Length + 1];
        parts[0] = Value;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        return new PlutoniumPath(Path.Combine(parts));
    }

    /// <inheritdoc/>
    public bool Equals(PlutoniumPath? other) =>
        other is not null &&
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PlutoniumPath);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Creates a <see cref="PlutoniumPath"/> from a string.</summary>
    /// <param name="value">The path string.</param>
    public static implicit operator PlutoniumPath(string value) => new(value);

    /// <summary>Extracts the path string from a <see cref="PlutoniumPath"/>.</summary>
    /// <param name="path">The path. Must not be null.</param>
    public static implicit operator string(PlutoniumPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Value;
    }

    /// <summary>Compares two paths for equality.</summary>
    public static bool operator ==(PlutoniumPath? left, PlutoniumPath? right) =>
        Equals(left, right);

    /// <summary>Compares two paths for inequality.</summary>
    public static bool operator !=(PlutoniumPath? left, PlutoniumPath? right) =>
        !Equals(left, right);
}
