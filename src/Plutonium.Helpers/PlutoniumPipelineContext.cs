using Plutonium.Core;

namespace Plutonium.Helpers;

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

/// <summary>
/// Live build services: git/gh/dotnet tool surfaces, logger, masker, host, and process runner.
/// </summary>
public sealed class PlutoniumBuildServices
{
    /// <summary>Gets the git surface. Never null.</summary>
    public PlutoniumGit Git { get; }

    /// <summary>Gets the gh release surface. Never null.</summary>
    public PlutoniumGh Gh { get; }

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

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumBuildServices"/> class.
    /// </summary>
    /// <param name="git">The git surface. Must not be null.</param>
    /// <param name="gh">The gh surface. Must not be null.</param>
    /// <param name="dotnet">The dotnet surface. Must not be null.</param>
    /// <param name="logger">The logger. Must not be null.</param>
    /// <param name="masker">The secret masker. Must not be null.</param>
    /// <param name="host">The host. Must not be null.</param>
    /// <param name="processRunner">The process runner. Must not be null.</param>
    public PlutoniumBuildServices(
        PlutoniumGit git,
        PlutoniumGh gh,
        PlutoniumDotNet dotnet,
        IPlutoniumLogger logger,
        PlutoniumSecretMasker masker,
        PlutoniumHost host,
        PlutoniumProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(gh);
        ArgumentNullException.ThrowIfNull(dotnet);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(masker);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(processRunner);
        Git = git;
        Gh = gh;
        DotNet = dotnet;
        Logger = logger;
        Masker = masker;
        Host = host;
        ProcessRunner = processRunner;
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
            new PlutoniumGh(processRunner, masker),
            new PlutoniumDotNet(processRunner, masker),
            logger,
            masker,
            host ?? new PlutoniumHost(logger),
            processRunner);
    }
}

/// <summary>
/// Pipeline context: the build instance, its options and services, pipeline type,
/// and shared per-run state. One context flows through local and pipeline runs.
/// </summary>
public sealed class PlutoniumPipelineContext
{
    /// <summary>Gets the build instance holding entry delegates. Never null.</summary>
    public object Build { get; }

    /// <summary>Gets the build options. Never null.</summary>
    public PlutoniumBuildOptions Options { get; }

    /// <summary>Gets the build services. Never null.</summary>
    public PlutoniumBuildServices Services { get; }

    /// <summary>Gets the pipeline type for this run.</summary>
    public PlutoniumPipelineType PipelineType { get; set; }

    /// <summary>Gets the discovered entries, set by setup. May be null before discovery.</summary>
    public PlutoniumAllEntries? Entries { get; set; }

    /// <summary>Gets or sets the pre-setup payload once computed.</summary>
    public PlutoniumPipelinePreSetup? PreSetup { get; set; }

    /// <summary>Gets the split <c>--args</c> map: AppId to bump expression. Never null.</summary>
    public IReadOnlyDictionary<string, string?> SplitArgs { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumPipelineContext"/> class.
    /// </summary>
    /// <param name="build">The build instance. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="services">The build services. Must not be null.</param>
    /// <param name="pipelineType">The pipeline type.</param>
    public PlutoniumPipelineContext(
        object build,
        PlutoniumBuildOptions options,
        PlutoniumBuildServices services,
        PlutoniumPipelineType pipelineType)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        Build = build;
        Options = options;
        Services = services;
        PipelineType = pipelineType;
    }

    /// <summary>Gets the Core logger shortcut.</summary>
    public IPlutoniumLogger Logger => Services.Logger;
}

/// <summary>
/// Contract every pipeline (GitHub, Azure, Local) implements: info detection,
/// pre-setup prepare/finalize, per-entry run prepare/finalize, post-setup
/// prepare/finalize, and workflow generation.
/// </summary>
public interface IPlutoniumPipeline
{
    /// <summary>Gets the pipeline type.</summary>
    PlutoniumPipelineType PipelineType { get; }

    /// <summary>
    /// Detects the pipeline info: branch, trigger, and pull-request number.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pipeline info. Never null.</returns>
    Task<PlutoniumPipelineInfo> GetPipelineInfoAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the pre-setup payload transported from the pre-setup job.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payload. Never null.</returns>
    Task<PlutoniumPipelinePreSetup> GetPipelinePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs pre-setup steps before the payload is computed (checkout setup, secret export).
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the preparation.</returns>
    Task PreparePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the computed payload to the pipeline environment.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="preSetup">The computed payload. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the finalization.</returns>
    Task FinalizePreSetupAsync(PlutoniumPipelineContext context, PlutoniumPipelinePreSetup preSetup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets up run contexts for one entry before it executes.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="entry">The entry. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the preparation.</returns>
    Task PrepareEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Packs artifacts after one entry executes.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="entry">The entry. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the finalization.</returns>
    Task FinalizeEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares post-setup (artifact unpack, release context load).
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the preparation.</returns>
    Task PreparePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes releases and stamps version markers after all entries finish.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task for the finalization.</returns>
    Task FinalizePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the workflow document plus the opaque emitter blocks for this pipeline.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document and emitter blocks. Never null.</returns>
    Task<PlutoniumWorkflowBuildResult> BuildWorkflowAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Workflow build result: the Core document plus the opaque emitter blocks.
/// </summary>
public sealed class PlutoniumWorkflowBuildResult
{
    /// <summary>Gets the workflow document. Never null.</summary>
    public PlutoniumWorkflowDocument Document { get; }

    /// <summary>Gets the first opaque emitter block (concurrency or trigger). May be null.</summary>
    public string? FirstBlock { get; }

    /// <summary>Gets the second opaque emitter block (permissions or pr). May be null.</summary>
    public string? SecondBlock { get; }

    /// <summary>Gets the third opaque emitter block (pool). May be null.</summary>
    public string? ThirdBlock { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumWorkflowBuildResult"/> class.
    /// </summary>
    /// <param name="document">The workflow document. Must not be null.</param>
    /// <param name="firstBlock">The first emitter block. May be null.</param>
    /// <param name="secondBlock">The second emitter block. May be null.</param>
    /// <param name="thirdBlock">The third emitter block. May be null.</param>
    public PlutoniumWorkflowBuildResult(
        PlutoniumWorkflowDocument document,
        string? firstBlock = null,
        string? secondBlock = null,
        string? thirdBlock = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        FirstBlock = firstBlock;
        SecondBlock = secondBlock;
        ThirdBlock = thirdBlock;
    }
}
