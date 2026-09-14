using System.Text.RegularExpressions;

namespace Plutonium.Helpers;

/// <summary>
/// Validated application identifier used to key multi-app builds.
/// Lowercase letters, digits, dots, and dashes; must start with a letter or digit.
/// </summary>
public sealed partial class PlutoniumAppId : IEquatable<PlutoniumAppId>
{
    /// <summary>Gets the validated identifier value.</summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumAppId"/> class.
    /// </summary>
    /// <param name="value">The identifier. Must satisfy the AppId rule.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> violates the rule.</exception>
    public PlutoniumAppId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (!AppIdPattern().IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"Invalid AppId '{value}'. Use lowercase letters, digits, dots, and dashes; start with a letter or digit.",
                nameof(value));
        }

        Value = trimmed;
    }

    /// <summary>Tries to create an identifier without throwing.</summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="appId">The identifier when valid; otherwise null.</param>
    /// <returns>True when <paramref name="value"/> satisfies the AppId rule.</returns>
    public static bool TryCreate(string? value, out PlutoniumAppId? appId)
    {
        appId = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!AppIdPattern().IsMatch(trimmed))
        {
            return false;
        }

        appId = new PlutoniumAppId(trimmed);
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(PlutoniumAppId? other) =>
        other is not null &&
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PlutoniumAppId);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>Creates an identifier from a string.</summary>
    /// <param name="value">The identifier string.</param>
    public static implicit operator PlutoniumAppId(string value) => new(value);

    /// <summary>Extracts the identifier string.</summary>
    /// <param name="appId">The identifier. Must not be null.</param>
    public static implicit operator string(PlutoniumAppId appId)
    {
        ArgumentNullException.ThrowIfNull(appId);
        return appId.Value;
    }

    /// <summary>Compares two identifiers for equality.</summary>
    public static bool operator ==(PlutoniumAppId? left, PlutoniumAppId? right) =>
        Equals(left, right);

    /// <summary>Compares two identifiers for inequality.</summary>
    public static bool operator !=(PlutoniumAppId? left, PlutoniumAppId? right) =>
        !Equals(left, right);

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdPattern();
}
