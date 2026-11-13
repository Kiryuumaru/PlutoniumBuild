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
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Pipelines.Context;
/// <summary>
/// Runtime options carried by the build instance: environment branches, repository paths,
/// tool surfaces, and output/cache roots. The build base supplies these; pipelines read them.
/// </summary>
public sealed class PlutoniumBuildOptions
{
    /// <summary>Gets the environment branches. Never null.</summary>
    public IReadOnlyList<string> EnvironmentBranches { get; }

    /// <summary>Gets the main environment branch. Never null.</summary>
    public string MainEnvironmentBranch { get; }

    /// <summary>Gets the repository root. Never null.</summary>
    public PlutoniumPath RepositoryRoot { get; }

    /// <summary>Gets the temporary directory for artifacts and notes. Never null.</summary>
    public PlutoniumPath TemporaryDirectory { get; }

    /// <summary>Gets the shared output root. Never null.</summary>
    public PlutoniumPath CommonOutputDirectory { get; }

    /// <summary>Gets the shared cache root. Never null.</summary>
    public PlutoniumPath CommonCacheDirectory { get; }

    /// <summary>Gets the repository HTTPS URL for release-note links. Never null.</summary>
    public string RepositoryHttpsUrl { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumBuildOptions"/> class.
    /// </summary>
    /// <param name="environmentBranches">The environment branches. Must not be null or empty.</param>
    /// <param name="mainEnvironmentBranch">The main branch. Must not be null or whitespace.</param>
    /// <param name="repositoryRoot">The repository root. Must not be null.</param>
    /// <param name="temporaryDirectory">The temporary directory. Must not be null.</param>
    /// <param name="commonOutputDirectory">The shared output root. Must not be null.</param>
    /// <param name="commonCacheDirectory">The shared cache root. Must not be null.</param>
    /// <param name="repositoryHttpsUrl">The repository HTTPS URL. Must not be null or whitespace.</param>
    public PlutoniumBuildOptions(
        IReadOnlyList<string> environmentBranches,
        string mainEnvironmentBranch,
        PlutoniumPath repositoryRoot,
        PlutoniumPath temporaryDirectory,
        PlutoniumPath commonOutputDirectory,
        PlutoniumPath commonCacheDirectory,
        string repositoryHttpsUrl)
    {
        ArgumentNullException.ThrowIfNull(environmentBranches);
        ArgumentException.ThrowIfNullOrWhiteSpace(mainEnvironmentBranch);
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        ArgumentNullException.ThrowIfNull(commonOutputDirectory);
        ArgumentNullException.ThrowIfNull(commonCacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryHttpsUrl);
        EnvironmentBranches = environmentBranches;
        MainEnvironmentBranch = mainEnvironmentBranch.Trim();
        RepositoryRoot = repositoryRoot;
        TemporaryDirectory = temporaryDirectory;
        CommonOutputDirectory = commonOutputDirectory;
        CommonCacheDirectory = commonCacheDirectory;
        RepositoryHttpsUrl = repositoryHttpsUrl.Trim();
    }
}
