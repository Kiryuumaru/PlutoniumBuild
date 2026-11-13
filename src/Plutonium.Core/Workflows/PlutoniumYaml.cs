using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows.Emitters;

namespace Plutonium.Core.Workflows;

/// <summary>
/// Opaque pre-rendered YAML block inserted verbatim by the emitter.
/// Carries no entry, AppId, or tag knowledge.
/// </summary>
public sealed class PlutoniumYamlRaw
{
    /// <summary>Gets the raw YAML text. Never null.</summary>
    public string Yaml { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumYamlRaw"/> class.
    /// </summary>
    /// <param name="yaml">Pre-rendered YAML text. Must not be null.</param>
    public PlutoniumYamlRaw(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        Yaml = yaml;
    }
}

/// <summary>
/// Minimal BCL-only YAML emitter: dictionaries, lists, and scalars to YAML text.
/// </summary>
/// <remarks>
/// Deterministic rules: 2-space indent, LF (<c>\n</c>) newlines, trailing newline.
/// A string is single-quoted when empty, has leading/trailing whitespace, contains
/// structural characters, contains <c>": "</c> or <c>" #"</c>, spans lines, or
/// matches a YAML 1.1 keyword (<c>on/off/yes/no/y/n/true/false/null/~</c>, any case).
/// Single quotes inside quoted scalars are doubled. <see cref="PlutoniumYamlRaw"/>
/// blocks are inserted verbatim with continuation lines indented to the current level.
/// </remarks>
public static class PlutoniumYaml
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "on", "off", "yes", "no", "y", "n", "true", "false", "null", "~",
    };

    /// <summary>
    /// Serializes a top-level mapping to YAML text with a trailing newline.
    /// </summary>
    /// <param name="document">The document mapping. Must not be null; keys must not be null.</param>
    /// <returns>YAML text with LF newlines. Never null.</returns>
    public static string SerializeDocument(IReadOnlyDictionary<string, object?> document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new System.Text.StringBuilder();
        AppendMapping(builder, document, 0);
        return builder.ToString();
    }

    /// <summary>
    /// Serializes an arbitrary value (mapping, sequence, scalar, or raw) at the given indent.
    /// </summary>
    /// <param name="value">The value to serialize. May be null.</param>
    /// <param name="indentLevel">The indent level in 2-space units. Must be zero or greater.</param>
    /// <returns>YAML text without a trailing newline guarantee. Never null.</returns>
    public static string SerializeValue(object? value, int indentLevel = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indentLevel);
        var builder = new System.Text.StringBuilder();
        AppendValue(builder, value, indentLevel);
        return builder.ToString();
    }

    /// <summary>
    /// Quotes a string scalar per the class rules.
    /// </summary>
    /// <param name="value">The value. Must not be null.</param>
    /// <returns>The possibly-quoted scalar. Never null.</returns>
    public static string QuoteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return NeedsQuotes(value) ? "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'" : value;
    }

    /// <summary>
    /// Indents every non-empty line of pre-rendered YAML to the given level.
    /// </summary>
    /// <param name="yaml">The YAML block. Must not be null.</param>
    /// <param name="indentLevel">The indent level in 2-space units. Must be zero or greater.</param>
    /// <returns>The indented block. Never null.</returns>
    public static string IndentRaw(string yaml, int indentLevel)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentOutOfRangeException.ThrowIfNegative(indentLevel);
        if (indentLevel == 0)
        {
            return yaml;
        }

        var prefix = new string(' ', indentLevel * 2);
        var lines = yaml.Split('\n');
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            builder.Append(line.Length == 0 ? line : prefix + line);
            if (i < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void AppendMapping(System.Text.StringBuilder builder, IReadOnlyDictionary<string, object?> mapping, int indentLevel)
    {
        if (mapping.Count == 0)
        {
            builder.Append(new string(' ', indentLevel * 2)).Append("{}\n");
            return;
        }

        var indent = new string(' ', indentLevel * 2);
        foreach (var pair in mapping)
        {
            ArgumentNullException.ThrowIfNull(pair.Key, nameof(mapping));
            var key = QuoteString(pair.Key);
            if (IsEmptyMapping(pair.Value))
            {
                builder.Append(indent).Append(key).Append(": {}\n");
            }
            else if (IsEmptySequence(pair.Value))
            {
                builder.Append(indent).Append(key).Append(": []\n");
            }
            else if (pair.Value is IReadOnlyDictionary<string, object?> nestedMapping)
            {
                builder.Append(indent).Append(key).Append(":\n");
                AppendMapping(builder, nestedMapping, indentLevel + 1);
            }
            else if (pair.Value is System.Collections.IDictionary dictionary)
            {
                builder.Append(indent).Append(key).Append(":\n");
                AppendDictionary(builder, dictionary, indentLevel + 1);
            }
            else if (pair.Value is System.Collections.IEnumerable enumerable and not string)
            {
                builder.Append(indent).Append(key).Append(":\n");
                AppendSequence(builder, enumerable, indentLevel + 1);
            }
            else if (pair.Value is PlutoniumYamlRaw raw && IsMultiline(raw.Yaml))
            {
                builder.Append(indent).Append(key).Append(":\n");
                builder.Append(IndentRaw(Normalize(raw.Yaml), indentLevel + 1)).Append('\n');
            }
            else
            {
                builder.Append(indent).Append(key).Append(": ");
                AppendScalar(builder, pair.Value);
                builder.Append('\n');
            }
        }
    }

    private static void AppendDictionary(System.Text.StringBuilder builder, System.Collections.IDictionary dictionary, int indentLevel)
    {
        if (dictionary.Count == 0)
        {
            builder.Append(new string(' ', indentLevel * 2)).Append("{}\n");
            return;
        }

        var indent = new string(' ', indentLevel * 2);
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            var key = QuoteString(Convert.ToString(entry.Value is null ? entry.Key : entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            var value = entry.Value;
            if (value is IReadOnlyDictionary<string, object?> nested)
            {
                builder.Append(indent).Append(key).Append(":\n");
                AppendMapping(builder, nested, indentLevel + 1);
            }
            else if (value is System.Collections.IEnumerable enumerable and not string)
            {
                builder.Append(indent).Append(key).Append(":\n");
                AppendSequence(builder, enumerable, indentLevel + 1);
            }
            else if (value is PlutoniumYamlRaw raw && IsMultiline(raw.Yaml))
            {
                builder.Append(indent).Append(key).Append(":\n");
                builder.Append(IndentRaw(Normalize(raw.Yaml), indentLevel + 1)).Append('\n');
            }
            else
            {
                builder.Append(indent).Append(key).Append(": ");
                AppendScalar(builder, value);
                builder.Append('\n');
            }
        }
    }

    private static void AppendSequence(System.Text.StringBuilder builder, System.Collections.IEnumerable sequence, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        var empty = true;
        foreach (var item in sequence)
        {
            empty = false;
            if (item is IReadOnlyDictionary<string, object?> mapping)
            {
                var nested = new System.Text.StringBuilder();
                AppendMapping(nested, mapping, indentLevel + 1);
                var text = nested.ToString().TrimEnd('\n', '\r');
                var lines = text.Split('\n');
                builder.Append(indent).Append("- ").Append(lines[0].TrimStart());
                builder.Append('\n');
                for (var i = 1; i < lines.Length; i++)
                {
                    builder.Append(lines[i].TrimEnd('\r')).Append('\n');
                }
            }
            else if (item is System.Collections.IDictionary dictionary)
            {
                var nested = new System.Text.StringBuilder();
                AppendDictionary(nested, dictionary, indentLevel + 1);
                var text = nested.ToString().TrimEnd('\n', '\r');
                var lines = text.Split('\n');
                builder.Append(indent).Append("- ").Append(lines[0].TrimStart());
                builder.Append('\n');
                for (var i = 1; i < lines.Length; i++)
                {
                    builder.Append(lines[i].TrimEnd('\r')).Append('\n');
                }
            }
            else if (item is PlutoniumYamlRaw raw)
            {
                var normalized = Normalize(raw.Yaml).TrimStart();
                if (normalized.StartsWith("-", StringComparison.Ordinal))
                {
                    builder.Append(IndentRaw(normalized, indentLevel)).Append('\n');
                }
                else if (IsMultiline(normalized))
                {
                    builder.Append(indent).Append("-\n");
                    builder.Append(IndentRaw(normalized, indentLevel + 1)).Append('\n');
                }
                else
                {
                    builder.Append(indent).Append("- ").Append(normalized).Append('\n');
                }
            }
            else if (item is System.Collections.IEnumerable nestedEnumerable and not string)
            {
                builder.Append(indent).Append("-\n");
                AppendSequence(builder, nestedEnumerable, indentLevel + 1);
            }
            else
            {
                builder.Append(indent).Append("- ");
                AppendScalar(builder, item);
                builder.Append('\n');
            }
        }

        if (empty)
        {
            builder.Append(indent).Append("[]\n");
        }
    }

    private static void AppendValue(System.Text.StringBuilder builder, object? value, int indentLevel)
    {
        if (value is IReadOnlyDictionary<string, object?> mapping)
        {
            AppendMapping(builder, mapping, indentLevel);
        }
        else if (value is System.Collections.IDictionary dictionary)
        {
            AppendDictionary(builder, dictionary, indentLevel);
        }
        else if (value is System.Collections.IEnumerable enumerable and not string)
        {
            AppendSequence(builder, enumerable, indentLevel);
        }
        else if (value is PlutoniumYamlRaw raw)
        {
            builder.Append(IndentRaw(Normalize(raw.Yaml), indentLevel));
        }
        else
        {
            builder.Append(new string(' ', indentLevel * 2));
            AppendScalar(builder, value);
            builder.Append('\n');
        }
    }

    private static void AppendScalar(System.Text.StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case bool flag:
                builder.Append(flag ? "true" : "false");
                break;
            case string text:
                builder.Append(QuoteString(text));
                break;
            case PlutoniumYamlRaw raw:
                builder.Append(Normalize(raw.Yaml).Trim());
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                builder.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case float single:
                builder.Append(single.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case double number:
                builder.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case decimal money:
                builder.Append(money.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append(QuoteString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
                break;
        }
    }

    private static bool IsEmptyMapping(object? value) =>
        (value is IReadOnlyDictionary<string, object?> readOnly && readOnly.Count == 0) ||
        (value is System.Collections.IDictionary dictionary && dictionary.Count == 0);

    private static bool IsEmptySequence(object? value) =>
        value is System.Collections.IEnumerable enumerable and not string &&
        value is not IReadOnlyDictionary<string, object?> &&
        value is not System.Collections.IDictionary &&
        value is not PlutoniumYamlRaw &&
        !enumerable.GetEnumerator().MoveNext();

    private static bool IsMultiline(string text) =>
        text.Contains('\n', StringComparison.Ordinal);

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n', '\r');

    private static bool NeedsQuotes(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        if (Keywords.Contains(value))
        {
            return true;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return true;
        }

        if (value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var ch in value)
        {
            switch (ch)
            {
                case ':':
                case '#':
                case ',':
                case '[':
                case ']':
                case '{':
                case '}':
                case '&':
                case '*':
                case '?':
                case '|':
                case '>':
                case '\'':
                case '"':
                case '%':
                case '@':
                case '`':
                case '!':
                case '\\':
                    return true;
            }
        }

        if (value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal))
        {
            return true;
        }

        if (value[0] is '-' or '<' or '=')
        {
            return true;
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '_' and not '.' and not '/' and not '-' and not ' ')
            {
                return true;
            }
        }

        return false;
    }
}
