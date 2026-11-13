using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Plutonium.Core.FileSystem;
using Plutonium.Core.Hosting;
using Plutonium.Core.Logging;
using Plutonium.Core.Process;

namespace Plutonium.Shell;

/// <summary>
/// Shell registration: wires one <see cref="PlutoniumShellState"/> per invocation
/// and registers every verb command plus the <c>--args</c> type parser.
/// </summary>
public sealed class PlutoniumShellSetup : ApplicationDependency
{
    private readonly Func<PlutoniumShellState> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumShellSetup"/> class.
    /// </summary>
    /// <param name="factory">The per-invocation state factory. Must not be null.</param>
    public PlutoniumShellSetup(Func<PlutoniumShellState> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc/>
    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(applicationBuilder);
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_factory());
    }
}

/// <summary>
/// Entry point for build authors: registers the Plutonium shell onto an
/// <see cref="ApplicationBuilder"/> with one line per build instance.
/// </summary>
public static class PlutoniumShellExtensions
{
    /// <summary>
    /// Registers all shell commands plus the <c>--args</c> parser and the
    /// per-invocation <see cref="PlutoniumShellState"/> for the given build.
    /// </summary>
    /// <param name="builder">The application builder. Must not be null.</param>
    /// <param name="build">The author build instance. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="logger">The logger, or null for a console logger.</param>
    /// <param name="processRunner">The process runner, or null for a default runner.</param>
    /// <param name="masker">The secret masker, or null for a fresh masker.</param>
    /// <param name="host">The host, or null for a default host.</param>
    /// <param name="logFile">Optional log file path enabling a file sink.</param>
    /// <param name="minimumLevel">The initial minimum log level.</param>
    /// <returns>The builder. Never null.</returns>
    public static ApplicationBuilder AddPlutoniumShell(
        this ApplicationBuilder builder,
        object build,
        Helpers.Pipelines.Context.PlutoniumBuildOptions options,
        IPlutoniumLogger? logger = null,
        PlutoniumProcessRunner? processRunner = null,
        PlutoniumSecretMasker? masker = null,
        PlutoniumHost? host = null,
        PlutoniumPath? logFile = null,
        PlutoniumLogLevel minimumLevel = PlutoniumLogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        var effectiveLogger = logger ?? CreateLogger(logFile, minimumLevel);
        var state = PlutoniumShellState.Create(build, options, effectiveLogger, processRunner, masker, host);
        builder.AddApplication<ApplicationBuilder>(new PlutoniumShellSetup(() => state));
        return builder
            .AddCommand<PlutoniumInteractiveCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumFetchCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumVersionCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumBumpCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumBumpAndForgetCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumBumpAndForgetAliasCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumStatusWatchCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumStatusWatchAliasCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumRunCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumTestCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumBuildCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumPublishCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumGitHubWorkflowCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumGithubWorkflowAliasCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumAzureWorkflowCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumAzureWorkflowAliasCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumPipelinePreSetupCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumPipelinePreSetupAliasCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumPipelinePostSetupCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumPipelinePostSetupAliasCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumPipelineEntryRunCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumPipelineEntryRunAliasCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumDeleteOriginTagsCommand, ApplicationBuilder>()
            .AddCommand<PlutoniumDeleteOriginTagsAliasCommand, ApplicationBuilder>()
            .AddCommandTypeParser<PlutoniumBumpArgParser, ApplicationBuilder>();
    }

    private static IPlutoniumLogger CreateLogger(PlutoniumPath? logFile, PlutoniumLogLevel minimumLevel)
    {
        if (logFile is null)
        {
            return new PlutoniumLogger([new PlutoniumConsoleSink()], minimumLevel);
        }

        return new PlutoniumLogger([new PlutoniumConsoleSink(), new PlutoniumFileSink(logFile)], minimumLevel);
    }
}
