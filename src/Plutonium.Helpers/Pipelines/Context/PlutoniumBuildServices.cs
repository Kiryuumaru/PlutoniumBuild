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
/// Live build services: git/gh/dotnet tool surfaces, terminal, logger, masker, host, and process runner.
/// </summary>
public sealed class PlutoniumBuildServices
{
    /// <summary>Gets the git surface. Never null.</summary>
    public PlutoniumGit Git { get; }

    /// <summary>Gets the gh release surface. Never null.</summary>
    public PlutoniumGitHubCli GitHubCli { get; }

    /// <summary>Gets the dotnet surface. Never null.</summary>
    public PlutoniumDotNet DotNet { get; }

    /// <summary>Gets the logger. Never null.</summary>
    public IPlutoniumLogger Logger { get; }

    /// <summary>Gets the secret masker. Never null.</summary>
    public PlutoniumSecretMasker Masker { get; }

    /// <summary>Gets the host for grouping and variable export. Never null.</summary>
    public PlutoniumHost Host { get; }

    /// <summary>Gets the process runner. Never null.</summary>
    public PlutoniumProcessRunner ProcessRunner { get; }

    /// <summary>Gets the terminal surface for running external tools. Never null.</summary>
    public PlutoniumTerminal Terminal { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumBuildServices"/> class.
    /// </summary>
    /// <param name="git">The git surface. Must not be null.</param>
    /// <param name="gitHubCli">The gh surface. Must not be null.</param>
    /// <param name="dotnet">The dotnet surface. Must not be null.</param>
    /// <param name="logger">The logger. Must not be null.</param>
    /// <param name="masker">The secret masker. Must not be null.</param>
    /// <param name="host">The host. Must not be null.</param>
    /// <param name="processRunner">The process runner. Must not be null.</param>
    public PlutoniumBuildServices(
        PlutoniumGit git,
        PlutoniumGitHubCli gitHubCli,
        PlutoniumDotNet dotnet,
        IPlutoniumLogger logger,
        PlutoniumSecretMasker masker,
        PlutoniumHost host,
        PlutoniumProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(gitHubCli);
        ArgumentNullException.ThrowIfNull(dotnet);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(masker);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(processRunner);
        Git = git;
        GitHubCli = gitHubCli;
        DotNet = dotnet;
        Logger = logger;
        Masker = masker;
        Host = host;
        ProcessRunner = processRunner;
        Terminal = new PlutoniumTerminal(processRunner, masker, logger);
    }

    /// <summary>
    /// Creates services wired to a single process runner, logger, and masker.
    /// </summary>
    /// <param name="processRunner">The process runner. Must not be null.</param>
    /// <param name="logger">The logger. Must not be null.</param>
    /// <param name="masker">The secret masker. Must not be null.</param>
    /// <param name="host">The host, or null for a default host.</param>
    /// <returns>The services. Never null.</returns>
    public static PlutoniumBuildServices Create(
        PlutoniumProcessRunner processRunner,
        IPlutoniumLogger logger,
        PlutoniumSecretMasker masker,
        PlutoniumHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(masker);
        return new PlutoniumBuildServices(
            new PlutoniumGit(processRunner, masker),
            new PlutoniumGitHubCli(processRunner, masker),
            new PlutoniumDotNet(processRunner, masker),
            logger,
            masker,
            host ?? new PlutoniumHost(logger),
            processRunner);
    }
}
