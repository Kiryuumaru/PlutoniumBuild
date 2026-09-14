using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// One interactive menu row: the display text plus the verb it invokes.
/// </summary>
public sealed class PlutoniumBuildOption
{
    /// <summary>Gets the option name. Never null.</summary>
    public string Name { get; }

    /// <summary>Gets the display text. Never null.</summary>
    public string DisplayText { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumBuildOption"/> class.
    /// </summary>
    /// <param name="name">The option name. Must not be null or whitespace.</param>
    /// <param name="displayText">The display text. Must not be null or whitespace.</param>
    public PlutoniumBuildOption(string name, string displayText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);
        Name = name.Trim();
        DisplayText = displayText.Trim();
    }
}

/// <summary>
/// Interactive console: TTY-first picker over runnable verbs, app plus part
/// prompts with <c>--args</c> non-interactive fallback, version-table snapshot
/// rendering, and the repaint-in-place status-watch loop. Prompts read from
/// standard input with no extra dependencies so CI stays non-interactive.
/// </summary>
public static class PlutoniumInteractiveConsole
{
    /// <summary>
    /// Runs the interactive picker: any <c>--args</c> presence skips the menu and
    /// forces the non-interactive <c>Run</c> verb; otherwise the verb menu prompts once.
    /// </summary>
    /// <remarks>
    /// StatusWatch live polling and step injection stay deferred.
    /// </remarks>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the interaction.</returns>
    public static async Task RunAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = MenuOptions();
        var selected = context.SplitArgs.Count == 0
            ? PromptSelection(options, "Select a task")
            : PlutoniumBuildVerbs.Run;
        await RunOptionAsync(context, selected, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prompts for bump args when <c>--args</c> is absent: one app plus one part
    /// expression. Falls back to the single known app without prompting for it.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="knownAppIds">Known lowercase AppIds. Must not be null or empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-app bump expressions. Never null.</returns>
    public static Task<IReadOnlyDictionary<string, string?>> PromptBumpArgsAsync(
        PlutoniumPipelineContext context,
        IReadOnlyList<string> knownAppIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(knownAppIds);
        if (knownAppIds.Count == 0)
        {
            throw new ArgumentException("At least one app is required.", nameof(knownAppIds));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var app = knownAppIds.Count == 1 ? knownAppIds[0] : PromptSelection(knownAppIds, "Select an app");
        var part = PromptLine("Bump part (major, minor, patch, prerelease, e.g. minor+1)");
        if (string.IsNullOrWhiteSpace(part))
        {
            throw new InvalidOperationException("Bump expression must not be empty.");
        }

        return Task.FromResult<IReadOnlyDictionary<string, string?>>(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [app] = part.Trim() });
    }

    /// <summary>
    /// Writes one per-app version table snapshot: header, separator, ascending versions.
    /// </summary>
    /// <param name="logger">The logger. Must not be null.</param>
    /// <param name="appId">The AppId key. Must not be null or whitespace.</param>
    /// <param name="allVersions">The reconciled taxonomy. Must not be null.</param>
    public static void WriteVersionTable(IPlutoniumLogger logger, string appId, PlutoniumAllVersions allVersions)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(allVersions);
        var lines = new List<string> { $"App {appId.Trim()}", "Environment | Latest" };
        lines.Add(new string('-', 24));
        foreach (var env in allVersions.EnvSorted)
        {
            var latest = allVersions.EnvVersionGrouped.TryGetValue(env, out var grouped) && grouped.Count != 0
                ? grouped[^1].WithoutMetadata().ToString()
                : "-";
            lines.Add(PadCell(env, 11) + " | " + latest);
        }

        foreach (var line in lines)
        {
            logger.Log(PlutoniumLogLevel.Information, line);
        }
    }

    /// <summary>
    /// Writes one watch-table tick: branch plus tick polling line.
    /// </summary>
    /// <param name="logger">The logger. Must not be null.</param>
    /// <param name="branch">The branch. Must not be null or whitespace.</param>
    /// <param name="tick">The zero-based tick.</param>
    public static void WriteWatchTable(IPlutoniumLogger logger, string branch, int tick)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        logger.Log(PlutoniumLogLevel.Information, $"[{branch.Trim()}] watch tick {tick}: polling runs...");
    }

    private static IReadOnlyList<PlutoniumBuildOption> MenuOptions() =>
    [
        new PlutoniumBuildOption(PlutoniumBuildVerbs.Run, "Run all entries"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.Test, "Test entries"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.Build, "Build entries"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.Publish, "Publish entries"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.Version, "Show versions"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.Bump, "Bump then watch"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.BumpAndForget, "Bump without watching"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.StatusWatch, "Watch in-flight runs"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.GithubWorkflow, "Regenerate GitHub workflow"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.AzureWorkflow, "Regenerate Azure workflow"),
        new PlutoniumBuildOption(PlutoniumBuildVerbs.Fetch, "Fetch from origin"),
    ];

    private static async Task RunOptionAsync(PlutoniumPipelineContext context, string selected, CancellationToken cancellationToken)
    {
        switch (selected.Trim())
        {
            case PlutoniumBuildVerbs.Run:
                await PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Test, PlutoniumEntryType.Build, PlutoniumEntryType.Publish], cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.Test:
                await PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Test], cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.Build:
                await PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Build], cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.Publish:
                await PlutoniumLocalPipeline.RunLocalAsync(context, [PlutoniumEntryType.Publish], cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.Version:
                await PlutoniumBuildVerbs.RunVersionAsync(context, cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.Bump:
                await PlutoniumBuildVerbs.RunBumpAsync(context, watch: true, cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.BumpAndForget:
                await PlutoniumBuildVerbs.RunBumpAsync(context, watch: false, cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.StatusWatch:
                await PlutoniumBuildVerbs.RunStatusWatchAsync(context, cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.GithubWorkflow:
                await PlutoniumBuildVerbs.RunGithubWorkflowAsync(context, cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.AzureWorkflow:
                await PlutoniumBuildVerbs.RunAzureWorkflowAsync(context, cancellationToken).ConfigureAwait(false);
                break;
            case PlutoniumBuildVerbs.Fetch:
                await PlutoniumBuildVerbs.RunFetchAsync(context, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unknown task '{selected}'.");
        }
    }

    private static string PromptSelection(IReadOnlyList<PlutoniumBuildOption> options, string title)
    {
        Console.WriteLine(title + ":");
        for (var index = 0; index < options.Count; index++)
        {
            Console.WriteLine($"  {index + 1}. {options[index].DisplayText}");
        }

        Console.Write("Enter a number: ");
        var line = Console.ReadLine();
        if (int.TryParse((line ?? string.Empty).Trim(), out var choice) && choice >= 1 && choice <= options.Count)
        {
            return options[choice - 1].Name;
        }

        throw new InvalidOperationException("No task selected.");
    }

    private static string PromptSelection(IReadOnlyList<string> options, string title)
    {
        Console.WriteLine(title + ":");
        for (var index = 0; index < options.Count; index++)
        {
            Console.WriteLine($"  {index + 1}. {options[index]}");
        }

        Console.Write("Enter a number: ");
        var line = Console.ReadLine();
        if (int.TryParse((line ?? string.Empty).Trim(), out var choice) && choice >= 1 && choice <= options.Count)
        {
            return options[choice - 1];
        }

        throw new InvalidOperationException("No option selected.");
    }

    private static string PromptLine(string title)
    {
        Console.Write(title + ": ");
        return Console.ReadLine() ?? string.Empty;
    }

    private static string PadCell(string value, int width)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= width ? trimmed : trimmed.PadRight(width);
    }
}
