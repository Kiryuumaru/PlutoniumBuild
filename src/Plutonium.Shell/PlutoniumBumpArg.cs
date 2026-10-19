using ApplicationBuilderHelpers.Abstracts;

namespace Plutonium.Shell;

/// <summary>
/// One <c>--args</c> split value: either <c>app=expr</c> or a bare expression
/// resolving to the sole known app downstream.
/// Single split value; distinct from <c>Plutonium.Helpers.Versioning.PlutoniumBumpArgs</c> (plural), the static version-expression mini-language that parses these expressions downstream.
/// </summary>
/// <param name="App">The app key, or empty for the bare-expression form.</param>
/// <param name="Expression">The bump expression, or null for the key-only shorthand.</param>
public sealed record PlutoniumBumpArg(string App, string? Expression)
{
    /// <summary>Formats the value back to CLI text.</summary>
    /// <returns>The CLI text. Never null.</returns>
    public override string ToString() =>
        Expression is null ? App : App + "=" + Expression;
}

/// <summary>
/// Parses <c>--args</c> text into <see cref="PlutoniumBumpArg"/>.
/// A null error accepts; a non-null error rejects with exit 2.
/// </summary>
public sealed class PlutoniumBumpArgParser : CommandTypeParser<PlutoniumBumpArg>
{
    /// <inheritdoc/>
    public override PlutoniumBumpArg? ParseValue(string? value, out string? validateError)
    {
        validateError = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            validateError = "Bump value must not be empty; want app=expr or a bare expression.";
            return default;
        }

        var text = value.Trim();
        var separator = text.IndexOf('=');
        if (separator < 0)
        {
            return new PlutoniumBumpArg(text, null);
        }

        var app = text[..separator].Trim();
        var expression = text[(separator + 1)..].Trim();
        if (app.Length == 0)
        {
            validateError = $"Invalid bump value '{value}': want app=expr or a bare expression.";
            return default;
        }

        return new PlutoniumBumpArg(app, expression.Length == 0 ? null : expression);
    }
}
