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
/// Options scoping one <see cref="PlutoniumAllVersions"/> reconciliation: which tags
/// to read, which branches count as environments, and how versions-file state merges.
/// Paths stay strings here; callers resolve them to <see cref="PlutoniumPath"/>.
/// </summary>
public sealed class PlutoniumVersionsOptions
{
    /// <summary>Gets the raw <c>git ls-remote --tags</c> output lines. Never null.</summary>
    public IReadOnlyList<string> LsRemoteLines { get; }

    /// <summary>Gets the environment branches. Never null or empty; entries must not be null or whitespace.</summary>
    public IReadOnlyList<string> EnvironmentBranches { get; }

    /// <summary>Gets the main environment branch. Never null or whitespace.</summary>
    public string MainEnvironmentBranch { get; }

    /// <summary>Gets the HEAD commit reconciled against tag commits. Never null or whitespace.</summary>
    public string HeadCommit { get; }

    /// <summary>Gets whether JSON file versioning merges file versions into the taxonomy. Defaults to false.</summary>
    public bool UseJsonFileVersioning { get; init; }

    /// <summary>Gets the raw versions-file map for merging. Null unless <see cref="UseJsonFileVersioning"/> applies.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? VersionsFileRaw { get; init; }

    /// <summary>Gets the known lowercase AppIds for versions-file validation. Never null.</summary>
    public IReadOnlyCollection<string> KnownAppIds { get; init; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumVersionsOptions"/> class.
    /// </summary>
    /// <param name="lsRemoteLines">The raw ls-remote lines. Must not be null; entries must not be null.</param>
    /// <param name="environmentBranches">The environment branches. Must not be null or empty.</param>
    /// <param name="mainEnvironmentBranch">The main branch. Must not be null or whitespace.</param>
    /// <param name="headCommit">The HEAD commit. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when branches or the HEAD commit are missing.</exception>
    public PlutoniumVersionsOptions(
        IReadOnlyList<string> lsRemoteLines,
        IReadOnlyList<string> environmentBranches,
        string mainEnvironmentBranch,
        string headCommit)
    {
        ArgumentNullException.ThrowIfNull(lsRemoteLines);
        foreach (var line in lsRemoteLines)
        {
            ArgumentNullException.ThrowIfNull(line, nameof(lsRemoteLines));
        }

        ArgumentNullException.ThrowIfNull(environmentBranches);
        if (environmentBranches.Count == 0)
        {
            throw new ArgumentException("At least one environment branch is required.", nameof(environmentBranches));
        }

        foreach (var branch in environmentBranches)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(branch, nameof(environmentBranches));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mainEnvironmentBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(headCommit);
        LsRemoteLines = lsRemoteLines;
        EnvironmentBranches = environmentBranches;
        MainEnvironmentBranch = mainEnvironmentBranch.Trim();
        HeadCommit = headCommit.Trim();
    }
}
