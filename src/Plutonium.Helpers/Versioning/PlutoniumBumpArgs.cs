using Plutonium.Core;
using Plutonium.Core.Runner;
using Plutonium.Core.Logging;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Process;
using Plutonium.Core.Process.Tools;
using Plutonium.Core.Hosting;
using Plutonium.Core.Workflows;
using Plutonium.Core.Workflows.Emitters;
using Plutonium.Helpers.Entries;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Versioning;

/// <summary>
/// <c>--args</c> bump grammar: <c>{appid=[major|minor|patch|prerelease|pre][&gt;|+]{n}}</c>,
/// comma-separated, highest rank applies last (prerelease before patch before minor
/// before major; assign <c>&gt;</c> outranks increment <c>+</c> within a part).
/// Parses to ordered <see cref="PlutoniumVersionBump"/> lists; application stays in
/// <see cref="PlutoniumSemVersionBumps"/>.
/// </summary>
public static class PlutoniumBumpArgs
{
    /// <summary>
    /// Parses one app's bump expression into rank-ordered bumps (ascending rank).
    /// </summary>
    /// <param name="expression">The bump expression (for example <c>minor+1,pre+2</c>). Must not be null or whitespace.</param>
    /// <returns>The ordered bumps. Never null.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="expression"/> holds an unknown part, amount, or extra segments.</exception>
    public static IReadOnlyList<PlutoniumVersionBump> Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var bumps = expression
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParsePart)
            .OrderBy(bump => bump.Rank)
            .ToList();
        if (bumps.Count == 0)
        {
            throw new ArgumentException("Bump expression must hold at least one part.", nameof(expression));
        }

        return bumps;
    }

    /// <summary>
    /// Parses the full <c>--args</c> map: AppId to bump expression, keys lowercased.
    /// A single bare expression without an AppId key maps to the sole known app.
    /// </summary>
    /// <param name="args">The raw args map. Must not be null.</param>
    /// <param name="knownAppIds">Known lowercase AppIds. Must not be null.</param>
    /// <returns>The per-app ordered bumps. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a bare expression meets multiple apps or an AppId is unknown.</exception>
    /// <exception cref="ArgumentException">Thrown when an expression is invalid.</exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<PlutoniumVersionBump>> ParseMap(
        IReadOnlyDictionary<string, string?> args,
        IReadOnlyCollection<string> knownAppIds)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(knownAppIds);

        var known = new HashSet<string>(knownAppIds.Select(id => id.ToLowerInvariant()), StringComparer.Ordinal);
        var effective = ExpandBareExpression(args, known);

        var result = new Dictionary<string, IReadOnlyList<PlutoniumVersionBump>>(StringComparer.Ordinal);
        foreach (var pair in effective)
        {
            var appId = pair.Key.Trim().ToLowerInvariant();
            if (!known.Contains(appId))
            {
                throw new InvalidOperationException($"App '{pair.Key.Trim()}' has no entry.");
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new ArgumentException($"Invalid bump value for app '{appId}'.", nameof(args));
            }

            result[appId] = Parse(pair.Value!.Trim());
        }

        return result;
    }

    /// <summary>
    /// Applies a bump expression to the latest version.
    /// </summary>
    /// <param name="latest">The latest version. Must not be null.</param>
    /// <param name="expression">The bump expression. Must not be null or whitespace.</param>
    /// <returns>The bumped version. Never null.</returns>
    public static PlutoniumSemVersion Apply(PlutoniumSemVersion latest, string expression)
    {
        ArgumentNullException.ThrowIfNull(latest);
        return PlutoniumSemVersionBumps.ApplyBumps(latest.WithoutMetadata().Clone(), Parse(expression));
    }

    private static IReadOnlyDictionary<string, string?> ExpandBareExpression(
        IReadOnlyDictionary<string, string?> args,
        HashSet<string> known)
    {
        if (args.Count == 1)
        {
            var single = args.First();
            if (!known.Contains(single.Key.Trim().ToLowerInvariant()) && string.IsNullOrWhiteSpace(single.Value))
            {
                if (known.Count != 1)
                {
                    throw new InvalidOperationException("A bare bump expression requires exactly one app entry.");
                }

                return new Dictionary<string, string?>(StringComparer.Ordinal) { [known.First()] = single.Key };
            }
        }

        return args;
    }

    private static PlutoniumVersionBump ParsePart(string part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(part, nameof(part));

        var trimmed = part.Trim().ToLowerInvariant();
        var isIncrement = true;
        string[] split;
        if (trimmed.Contains('+', StringComparison.Ordinal))
        {
            split = trimmed.Split('+');
        }
        else if (trimmed.Contains('>', StringComparison.Ordinal))
        {
            isIncrement = false;
            split = trimmed.Split('>');
        }
        else
        {
            split = [trimmed];
        }

        try
        {
            if (split.Length != 2 && split.Length != 1)
            {
                throw new ArgumentException($"Invalid bump value '{part}'.");
            }

            var amount = split.Length > 1 ? int.Parse(split[1].Trim(), System.Globalization.CultureInfo.InvariantCulture) : 1;
            if (amount <= 0)
            {
                throw new ArgumentException($"Invalid bump value '{part}'.");
            }

            return split[0].Trim() switch
            {
                "major" => new PlutoniumVersionBump(
                    PlutoniumVersionPart.Major, isIncrement, amount, isIncrement ? 6 : 7),
                "minor" => new PlutoniumVersionBump(
                    PlutoniumVersionPart.Minor, isIncrement, amount, isIncrement ? 4 : 5),
                "patch" => new PlutoniumVersionBump(
                    PlutoniumVersionPart.Patch, isIncrement, amount, isIncrement ? 2 : 3),
                "prerelease" or "pre" => new PlutoniumVersionBump(
                    PlutoniumVersionPart.Prerelease, isIncrement, amount, isIncrement ? 0 : 1),
                _ => throw new ArgumentException($"Invalid bump value '{part}'."),
            };
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"Invalid bump value '{part}'.", ex);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException($"Invalid bump value '{part}'.", ex);
        }
    }
}
