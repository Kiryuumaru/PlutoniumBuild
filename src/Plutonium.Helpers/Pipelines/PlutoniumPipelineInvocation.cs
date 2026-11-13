using System.Text.RegularExpressions;

namespace Plutonium.Helpers.Pipelines;

/// <summary>
/// Emitted-invocation composer for generated pipeline files: owns the shell
/// command tokens CI steps append to the runner script and composes the
/// per-step run lines. Kebab command spellings are emission-only; the Pascal
/// aliases stay human-only.
/// </summary>
public static partial class PlutoniumPipelineInvocation
{
    /// <summary>Gets the pre-setup command token appended to the runner script.</summary>
    public const string PreSetupCommand = "shell pipeline-pre-setup";

    /// <summary>Gets the post-setup command token appended to the runner script.</summary>
    public const string PostSetupCommand = "shell pipeline-post-setup";

    /// <summary>Gets the entry-run command token appended to the runner script.</summary>
    public const string EntryRunCommand = "shell pipeline-entry-run";

    /// <summary>
    /// Composes the pre-setup step run line: the runner script plus
    /// <see cref="PreSetupCommand"/>.
    /// </summary>
    /// <param name="runScript">The runner script. Must not be null or whitespace.</param>
    /// <returns>The composed run line. Never null.</returns>
    public static string ComposePreSetup(string runScript) => Compose(runScript, PreSetupCommand);

    /// <summary>
    /// Composes the post-setup step run line: the runner script plus
    /// <see cref="PostSetupCommand"/>.
    /// </summary>
    /// <param name="runScript">The runner script. Must not be null or whitespace.</param>
    /// <returns>The composed run line. Never null.</returns>
    public static string ComposePostSetup(string runScript) => Compose(runScript, PostSetupCommand);

    /// <summary>
    /// Composes the entry-run step run line: the runner script plus
    /// <see cref="EntryRunCommand"/>. The entry selector travels in the
    /// <c>PLUTONIUM_ENTRY</c> job variable, never in the composed command, so no
    /// quoting survives across operating systems.
    /// </summary>
    /// <param name="runScript">The runner script. Must not be null or whitespace.</param>
    /// <param name="entryId">The entry id, validated for generated-workflow safety. Must not be null or whitespace.</param>
    /// <returns>The composed run line. Never null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="entryId"/> is not valid for generated workflows.</exception>
    public static string ComposeEntryRun(string runScript, string entryId)
    {
        ValidateEntryId(entryId);
        return Compose(runScript, EntryRunCommand);
    }

    /// <summary>
    /// Validates an entry id for generated workflows: GitHub job-id-safe letters,
    /// digits, underscores, and hyphens, starting with a letter or underscore.
    /// </summary>
    /// <param name="entryId">The entry id. Must not be null or whitespace.</param>
    /// <exception cref="InvalidOperationException">Thrown when the id is not valid for generated workflows.</exception>
    public static void ValidateEntryId(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var trimmed = entryId.Trim();
        if (!EntryIdPattern().IsMatch(trimmed))
        {
            throw new InvalidOperationException(
                $"Invalid entry id '{trimmed}'. Use letters, digits, underscores, or hyphens; start with a letter or underscore.");
        }
    }

    private static string Compose(string runScript, string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runScript);
        return runScript.Trim() + " " + command;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryIdPattern();
}
