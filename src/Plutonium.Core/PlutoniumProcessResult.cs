namespace Plutonium.Core;

/// <summary>
/// Frozen result of a finished external process invocation.
/// Carries the exit code plus captured standard output and error.
/// </summary>
public sealed class PlutoniumProcessResult : IEquatable<PlutoniumProcessResult>
{
    /// <summary>Gets the process exit code. Zero indicates success.</summary>
    public int ExitCode { get; }

    /// <summary>Gets the captured standard output. Never null.</summary>
    public string StandardOutput { get; }

    /// <summary>Gets the captured standard error. Never null.</summary>
    public string StandardError { get; }

    /// <summary>Gets whether the process exited with code zero.</summary>
    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumProcessResult"/> class.
    /// </summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="standardOutput">Captured standard output. Must not be null.</param>
    /// <param name="standardError">Captured standard error. Must not be null.</param>
    public PlutoniumProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> when the exit code is non-zero.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="ExitCode"/> is non-zero.</exception>
    public void EnsureSucceeded()
    {
        if (!Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(StandardError)
                ? string.Empty
                : $": {StandardError.Trim()}";
            throw new InvalidOperationException(
                $"Process failed with exit code {ExitCode}{detail}");
        }
    }

    /// <inheritdoc/>
    public bool Equals(PlutoniumProcessResult? other) =>
        other is not null &&
        ExitCode == other.ExitCode &&
        string.Equals(StandardOutput, other.StandardOutput, StringComparison.Ordinal) &&
        string.Equals(StandardError, other.StandardError, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PlutoniumProcessResult);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(ExitCode, StandardOutput, StandardError);
}
