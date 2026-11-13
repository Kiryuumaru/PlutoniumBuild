using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Attributes;
using Microsoft.Extensions.Hosting;

namespace Plutonium.Shell;

/// <summary>
/// Default root command: runs the interactive picker over runnable verbs.
/// Reached with no arguments; accepts the shared shell options.
/// Derives from PlutoniumShellCommand directly (not PlutoniumShellGroup) because it is the unnamed root default command while all other verbs derive from PlutoniumShellGroup: do not normalize the inheritance without preserving default routing.
/// </summary>
[Command(Description = "Interactive picker over runnable tasks (default verb).")]
public sealed class PlutoniumInteractiveCommand : PlutoniumShellCommand
{
    /// <inheritdoc/>
    protected override string Verb => Helpers.App.PlutoniumBuildVerbs.Interactive;

    /// <inheritdoc/>
    protected override ValueTask Run(ApplicationHost<HostApplicationBuilder> host, CancellationToken cancellationToken)
    {
        GuardInteractive();
        return base.Run(host, cancellationToken);
    }
}
