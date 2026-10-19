using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Attributes;
using ApplicationBuilderHelpers.Exceptions;
using Microsoft.Extensions.Hosting;

namespace Plutonium.Shell;

/// <summary>
/// Shared shell base: option storage plus the verb execution all commands share.
/// Options live once on <see cref="PlutoniumShellGroup"/> so ABH binds them on the
/// group node and inherits them per verb.
/// </summary>
public abstract class PlutoniumShellCommand : Command
{
    /// <summary>Gets or sets the targets excluded via repeatable <c>--skip</c>.</summary>
    [CommandOption("skip", Description = "Skip targets (repeatable).")]
    public List<string> Skip { get; set; } = [];

    /// <summary>Gets or sets the <c>--args</c> split values (repeatable).</summary>
    [CommandOption("args", Description = "App bump args as app=expr (repeatable).")]
    public List<PlutoniumBumpArg> Args { get; set; } = [];

    /// <summary>Gets or sets whether <c>--list</c> lists targets instead of running.</summary>
    [CommandOption("list", Description = "List targets instead of running.")]
    public bool List { get; set; }

    /// <summary>Gets or sets whether <c>--all</c> includes unlisted targets in <c>--list</c>.</summary>
    [CommandOption("all", Description = "Include unlisted targets in --list.")]
    public bool All { get; set; }

    /// <summary>Gets the verb this command runs. Never null.</summary>
    protected abstract string Verb { get; }

    /// <inheritdoc/>
    protected override async ValueTask Run(ApplicationHost<HostApplicationBuilder> host, CancellationToken cancellationToken)
    {
        var state = PlutoniumShellState.Resolve(host.Services);
        foreach (var value in Skip)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CommandException("Option '--skip' must not be empty.", 2, CommandErrorKind.InvalidValue);
            }
        }

        var skipped = Skip.Select(value => value.Trim()).ToList();
        if (List)
        {
            state.ListTargets(All);
            return;
        }

        state.ApplySplitArgs(Args, state.KnownAppIds());
        var result = await state.RunVerbAsync(Verb, skipped, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new CommandException($"Target '{Verb}' failed.", 1);
        }
    }

    /// <summary>
    /// Rejects prompting when stdin is redirected: CI must fail fast instead of blocking.
    /// </summary>
    protected void GuardInteractive()
    {
        if (!List && Args.Count == 0 && Console.IsInputRedirected)
        {
            throw new CommandException("Interactive mode requires a console; pass --args or run a verb directly in CI.", 2, CommandErrorKind.InvalidValue);
        }
    }
}

/// <summary>
/// The <c>shell</c> group node owning every shared option once: ABH binds the
/// options here and inherits them into each verb command underneath.
/// ABH group-inheritance binds the shared options (--skip/--args/--list/--all) on the group and inherits them onto every verb, so --args on non-bump verbs is framework-inherited help noise (empty Args is a no-op at runtime): do not fix without rearchitecting the option inheritance.
/// </summary>
[Command("shell", "Plutonium build commands.")]
public abstract class PlutoniumShellGroup : PlutoniumShellCommand
{
}
