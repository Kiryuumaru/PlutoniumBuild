namespace Plutonium.Core;

/// <summary>
/// Marks a CLI parameter as required: the run fails when no value is supplied.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PlutoniumRequiredAttribute : Attribute
{
}

/// <summary>
/// Marks a CLI parameter as secret: its value is masked in all log output.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PlutoniumSecretAttribute : Attribute
{
}

/// <summary>
/// Declares a named CLI parameter bound from <c>--name value</c> arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class PlutoniumParameterAttribute : Attribute
{
    /// <summary>Gets the parameter name as it appears on the command line.</summary>
    public string Name { get; }

    /// <summary>Gets the human-readable description shown in help.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumParameterAttribute"/> class.
    /// </summary>
    /// <param name="name">The parameter name. Must not be null or whitespace.</param>
    public PlutoniumParameterAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }
}

/// <summary>
/// Built-in runner options always recognized by the CLI parser:
/// target selection, skip replanning, and listing.
/// </summary>
public sealed class PlutoniumCliOptions
{
    /// <summary>Gets the requested target names, in command-line order.</summary>
    public IReadOnlyList<string> Targets { get; }

    /// <summary>Gets the target names excluded via <c>--skip</c>.</summary>
    public IReadOnlyList<string> Skipped { get; }

    /// <summary>Gets whether <c>--list</c> was passed.</summary>
    public bool List { get; }

    /// <summary>Gets whether <c>--help</c> was passed.</summary>
    public bool Help { get; }

    /// <summary>Gets the raw parameter values keyed by lowercase name.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumCliOptions"/> class.
    /// </summary>
    /// <param name="targets">Requested targets. Must not be null.</param>
    /// <param name="skipped">Skipped targets. Must not be null.</param>
    /// <param name="list">Whether listing was requested.</param>
    /// <param name="help">Whether help was requested.</param>
    /// <param name="parameters">Parameter values. Must not be null.</param>
    public PlutoniumCliOptions(
        IReadOnlyList<string> targets,
        IReadOnlyList<string> skipped,
        bool list,
        bool help,
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(skipped);
        ArgumentNullException.ThrowIfNull(parameters);
        Targets = targets;
        Skipped = skipped;
        List = list;
        Help = help;
        Parameters = parameters;
    }
}

/// <summary>
/// Parses command-line arguments into <see cref="PlutoniumCliOptions"/>.
/// Unknown <c>--</c> arguments fail with a did-you-mean suggestion.
/// </summary>
public static class PlutoniumCliParser
{
    private static readonly HashSet<string> KnownFlags =
        new(StringComparer.OrdinalIgnoreCase) { "skip", "list", "help" };

    /// <summary>
    /// Parses the given arguments against the known parameter names.
    /// </summary>
    /// <param name="args">Raw command-line arguments. Must not be null.</param>
    /// <param name="knownParameters">Declared parameter names. Must not be null.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="PlutoniumCliException">Thrown on unknown arguments or missing values.</exception>
    public static PlutoniumCliOptions Parse(string[] args, IEnumerable<string> knownParameters)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(knownParameters);

        var known = new HashSet<string>(knownParameters, StringComparer.OrdinalIgnoreCase);
        foreach (var flag in KnownFlags)
        {
            known.Add(flag);
        }

        var targets = new List<string>();
        var skipped = new List<string>();
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                targets.Add(token);
                continue;
            }

            var name = token[2..];
            if (name.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                list = true;
                continue;
            }

            if (name.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                help = true;
                continue;
            }

            if (name.Equals("skip", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                if (i >= args.Length)
                {
                    throw new PlutoniumCliException("Missing value for --skip.");
                }

                skipped.Add(args[i]);
                continue;
            }

            if (!known.Contains(name))
            {
                throw new PlutoniumCliException(
                    $"Unknown argument '--{name}'.{Suggest(name, known)}");
            }

            i++;
            if (i >= args.Length)
            {
                throw new PlutoniumCliException($"Missing value for '--{name}'.");
            }

            parameters[name.ToLowerInvariant()] = args[i];
        }

        return new PlutoniumCliOptions(targets, skipped, list, help, parameters);
    }

    private static string Suggest(string unknown, IEnumerable<string> known)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in known)
        {
            var distance = EditDistance(
                unknown.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best is not null && bestDistance <= Math.Max(2, unknown.Length / 2)
            ? $" Did you mean '--{best}'?"
            : string.Empty;
    }

    private static int EditDistance(string left, string right)
    {
        var matrix = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++)
        {
            matrix[i, 0] = i;
        }

        for (var j = 0; j <= right.Length; j++)
        {
            matrix[0, j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[left.Length, right.Length];
    }
}

/// <summary>
/// Error raised for malformed command-line input.
/// </summary>
public sealed class PlutoniumCliException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumCliException"/> class.
    /// </summary>
    /// <param name="message">The error message. Must not be null.</param>
    public PlutoniumCliException(string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(message);
    }
}
