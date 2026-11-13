using Plutonium.Core.Hosting;
using Plutonium.Core.Logging;
using Plutonium.Core.Process;
using Plutonium.Core.Runner;
using Plutonium.Helpers.App;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Runtime;

namespace Plutonium.Shell;

/// <summary>
/// Per-invocation shell state: the build instance, options, services, context,
/// runner, and secret wiring for one command run.
/// </summary>
public sealed class PlutoniumShellState
{
    private readonly PlutoniumPipelineContext _context;
    private readonly PlutoniumRunner _runner;
    private readonly IPlutoniumLogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumShellState"/> class.
    /// </summary>
    /// <param name="build">The author build instance. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="services">The build services. Must not be null.</param>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="runner">The runner over verb targets. Must not be null.</param>
    /// <param name="logger">The logger. Must not be null.</param>
    public PlutoniumShellState(
        object build,
        PlutoniumBuildOptions options,
        PlutoniumBuildServices services,
        PlutoniumPipelineContext context,
        PlutoniumRunner runner,
        IPlutoniumLogger logger)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(logger);
        Build = build;
        Options = options;
        Services = services;
        _context = context;
        _runner = runner;
        _logger = logger;
    }

    /// <summary>Gets the author build instance. Never null.</summary>
    public object Build { get; }

    /// <summary>Gets the build options. Never null.</summary>
    public PlutoniumBuildOptions Options { get; }

    /// <summary>Gets the build services. Never null.</summary>
    public PlutoniumBuildServices Services { get; }

    /// <summary>Gets the pipeline context. Never null.</summary>
    public PlutoniumPipelineContext Context => _context;

    /// <summary>Gets the runner. Never null.</summary>
    public PlutoniumRunner Runner => _runner;

    /// <summary>Gets the logger. Never null.</summary>
    public IPlutoniumLogger Logger => _logger;

    /// <summary>
    /// Resolves the shell state from the host service provider.
    /// </summary>
    /// <param name="host">The application host. Must not be null.</param>
    /// <returns>The shell state. Never null.</returns>
    public static PlutoniumShellState Resolve(IServiceProvider host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var state = (PlutoniumShellState?)host.GetService(typeof(PlutoniumShellState));
        ArgumentNullException.ThrowIfNull(state);
        return state;
    }

    /// <summary>
    /// Creates shell state from explicit parts: wires secrets, context, and runner.
    /// </summary>
    /// <param name="build">The author build instance. Must not be null.</param>
    /// <param name="options">The build options. Must not be null.</param>
    /// <param name="logger">The logger. Must not be null.</param>
    /// <param name="processRunner">The process runner, or null for a default runner.</param>
    /// <param name="masker">The secret masker, or null for a fresh masker.</param>
    /// <param name="host">The host, or null for a default host.</param>
    /// <returns>The shell state. Never null.</returns>
    public static PlutoniumShellState Create(
        object build,
        PlutoniumBuildOptions options,
        IPlutoniumLogger logger,
        PlutoniumProcessRunner? processRunner = null,
        PlutoniumSecretMasker? masker = null,
        PlutoniumHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        var effectiveMasker = masker ?? new PlutoniumSecretMasker();
        var effectiveRunner = processRunner ?? new PlutoniumProcessRunner(logger);
        var effectiveHost = host ?? new PlutoniumHost(logger);
        var services = PlutoniumBuildServices.Create(effectiveRunner, logger, effectiveMasker, effectiveHost);
        PlutoniumSecrets.Setup(build, logger, effectiveHost.ExportVariable, effectiveMasker);
        var context = new PlutoniumPipelineContext(
            build, options, services, PlutoniumPipelineDetection.DetectPipelineType());
        var runner = new PlutoniumRunner(PlutoniumBuildVerbs.BuildTargets(context), logger);
        return new PlutoniumShellState(build, options, services, context, runner, logger);
    }

    /// <summary>
    /// Applies parsed <c>--args</c> values onto the context split-args map.
    /// </summary>
    /// <param name="args">The parsed args. Must not be null.</param>
    /// <param name="knownAppIds">Known lowercase AppIds for bare-expression detection. Null skips the check.</param>
    public void ApplySplitArgs(IReadOnlyList<PlutoniumBumpArg> args, IReadOnlyCollection<string>? knownAppIds = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var arg in args)
        {
            ArgumentNullException.ThrowIfNull(arg);
            var app = arg.App.Trim();
            if (arg.Expression is null && !IsKnownApp(app, knownAppIds) && !IsBareExpression(arg, knownAppIds))
            {
                throw new ApplicationBuilderHelpers.Exceptions.CommandException(
                    $"Bare bump expression '{arg.App}' requires exactly one app entry; use app=expr instead.",
                    2,
                    ApplicationBuilderHelpers.Exceptions.CommandErrorKind.InvalidValue);
            }

            if (!Plutonium.Helpers.App.PlutoniumAppId.TryCreate(app, out _) && !IsBareExpression(arg, knownAppIds))
            {
                var known = knownAppIds is null || knownAppIds.Count != 1
                    ? "use app=expr instead"
                    : $"did you mean '{knownAppIds.First().Trim()}=<expr>'?";
                throw new ApplicationBuilderHelpers.Exceptions.CommandException(
                    $"Invalid bump value '{arg.App}'. {known}.",
                    2,
                    ApplicationBuilderHelpers.Exceptions.CommandErrorKind.InvalidValue);
            }

            map[app] = arg.Expression;
        }

        _context.SplitArgs = map;
    }

    private static bool IsKnownApp(string app, IReadOnlyCollection<string>? knownAppIds)
    {
        if (knownAppIds is null)
        {
            return false;
        }

        var normalized = app.Trim().ToLowerInvariant();
        return knownAppIds.Any(known => string.Equals(known.Trim().ToLowerInvariant(), normalized, StringComparison.Ordinal));
    }

    private static bool IsBareExpression(PlutoniumBumpArg arg, IReadOnlyCollection<string>? knownAppIds)
    {
        if (arg.Expression is not null || knownAppIds is null)
        {
            return false;
        }

        if (IsKnownApp(arg.App, knownAppIds))
        {
            return false;
        }

        if (knownAppIds.Count != 1)
        {
            return false;
        }

        var sole = knownAppIds.First().Trim().ToLowerInvariant();
        return !Plutonium.Helpers.App.PlutoniumAppId.TryCreate(arg.App.Trim(), out _)
            || !string.Equals(arg.App.Trim().ToLowerInvariant(), sole, StringComparison.Ordinal);
    }

    /// <summary>
    /// Executes one verb through the runner.
    /// </summary>
    /// <param name="verb">The verb target name. Must not be null.</param>
    /// <param name="skipped">The skipped targets. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run result. Never null.</returns>
    public async Task<PlutoniumRunResult> RunVerbAsync(
        string verb,
        IReadOnlyList<string> skipped,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        ArgumentNullException.ThrowIfNull(skipped);
        try
        {
            return await _runner.RunAsync([verb.Trim()], skipped, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new ApplicationBuilderHelpers.Exceptions.CommandException(
                ex.Message,
                1,
                ApplicationBuilderHelpers.Exceptions.CommandErrorKind.Fault);
        }
    }

    /// <summary>
    /// Lists target names, honoring the unlisted filter.
    /// </summary>
    /// <param name="includeUnlisted">Whether to include unlisted targets.</param>
    /// <returns>The target names. Never null.</returns>
    public IReadOnlyList<string> ListTargets(bool includeUnlisted = false)
    {
        var names = _runner.ListTargets(includeUnlisted);
        foreach (var name in names)
        {
            _logger.Log(PlutoniumLogLevel.Information, name);
        }

        return names;
    }

    /// <summary>
    /// Resolves the known lowercase AppIds from discovered entries, discovering
    /// them when the context has none yet. Falls back to empty on discovery errors.
    /// </summary>
    /// <returns>The known AppIds. Never null.</returns>
    public IReadOnlyCollection<string> KnownAppIds()
    {
        var entries = _context.Entries;
        if (entries is null)
        {
            try
            {
                entries = Plutonium.Helpers.Entries.Discovery.PlutoniumEntryDiscovery
                    .GetAllAsync(Build).GetAwaiter().GetResult();
                _context.Entries = entries;
            }
            catch (InvalidOperationException)
            {
                return [];
            }
        }

        return entries.AppEntryMap.Keys.ToList();
    }
}
