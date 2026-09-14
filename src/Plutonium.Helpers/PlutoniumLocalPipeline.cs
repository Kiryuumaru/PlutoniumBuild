using Plutonium.Core;

namespace Plutonium.Helpers;

/// <summary>
/// Local pipeline: direct in-process execution with no YAML, no matrix export,
/// and local trigger semantics. <c>Run/Test/Build/Publish</c> execute entries
/// filtered by <c>RunType</c> flags containing <c>Local</c> and <c>Condition</c>;
/// artifact pack/unpack stays active through temp roots so multi-entry data flow
/// works on a dev machine.
/// </summary>
public sealed class PlutoniumLocalPipeline : IPlutoniumPipeline
{
    /// <inheritdoc/>
    public PlutoniumPipelineType PipelineType => PlutoniumPipelineType.Local;

    /// <inheritdoc/>
    public Task<PlutoniumPipelineInfo> GetPipelineInfoAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PlutoniumPipelineInfo(context.Options.MainEnvironmentBranch, PlutoniumTriggerType.Local));
    }

    /// <inheritdoc/>
    public Task<PlutoniumPipelinePreSetup> GetPipelinePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.PreSetup is not null)
        {
            return Task.FromResult(context.PreSetup);
        }

        throw new InvalidOperationException("Local pre-setup payload has not been computed.");
    }

    /// <inheritdoc/>
    public Task PreparePreSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FinalizePreSetupAsync(PlutoniumPipelineContext context, PlutoniumPipelinePreSetup preSetup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preSetup);
        cancellationToken.ThrowIfCancellationRequested();
        context.PreSetup = preSetup;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task PrepareEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task FinalizeEntryRunAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entry);
        await PlutoniumArtifacts.PackArtifactsAsync(
            entry, context.Options.CommonOutputDirectory, context.Options.TemporaryDirectory, context.Services.Logger, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task PreparePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await PlutoniumArtifacts.UnpackArtifactsAsync(
            context.Options.TemporaryDirectory,
            context.Options.CommonOutputDirectory,
            appIds: null,
            context.Services.Logger,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task FinalizePostSetupAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<PlutoniumWorkflowBuildResult> BuildWorkflowAsync(PlutoniumPipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Local runs do not emit workflow files.");
    }

    /// <summary>
    /// Runs local entry kinds in DAG order: <c>pre_test</c>, <c>build</c>,
    /// <c>post_test</c>, <c>publish</c>. Entries whose run types exclude local
    /// or whose condition is false are skipped silently.
    /// </summary>
    /// <param name="context">The pipeline context. Must not be null.</param>
    /// <param name="kinds">The entry kinds to run. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The executed entries. Never null.</returns>
    public static async Task<IReadOnlyList<IRunEntryDefinition>> RunLocalAsync(
        PlutoniumPipelineContext context,
        IEnumerable<PlutoniumEntryType> kinds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(kinds);
        var selected = new HashSet<PlutoniumEntryType>(kinds);
        var run = new PlutoniumPipelineRun(new PlutoniumLocalPipeline(), await new PlutoniumLocalPipeline().GetPipelineInfoAsync(context, cancellationToken).ConfigureAwait(false), PlutoniumPipelineType.Local);
        await PlutoniumPreSetup.RunPreSetupAsync(context, run, cancellationToken).ConfigureAwait(false);
        var entries = context.Entries ?? await PlutoniumEntryDiscovery.GetAllAsync(context.Build, cancellationToken).ConfigureAwait(false);
        context.Entries = entries;
        var ordered = new List<IRunEntryDefinition>();
        var preTests = new List<IRunEntryDefinition>();
        var postTests = new List<IRunEntryDefinition>();
        if (selected.Contains(PlutoniumEntryType.Test))
        {
            foreach (var test in entries.TestEntries.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsBeforeBuildAsync(test, cancellationToken).ConfigureAwait(false))
                {
                    preTests.Add(test);
                }
                else
                {
                    postTests.Add(test);
                }
            }
        }

        ordered.AddRange(preTests);

        if (selected.Contains(PlutoniumEntryType.Build))
        {
            ordered.AddRange(entries.BuildEntries.Values.Cast<IRunEntryDefinition>());
        }

        ordered.AddRange(postTests);

        if (selected.Contains(PlutoniumEntryType.Publish))
        {
            ordered.AddRange(entries.PublishEntries.Values.Cast<IRunEntryDefinition>());
        }

        var executed = new List<IRunEntryDefinition>();
        foreach (var entry in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await RunWhenLocalAsync(context, entry, cancellationToken).ConfigureAwait(false))
            {
                executed.Add(entry);
            }
        }

        await PlutoniumPreSetup.RunPostSetupAsync(context, run, cancellationToken).ConfigureAwait(false);
        return executed;
    }

    private static async Task<bool> IsBeforeBuildAsync(ITestEntryDefinition entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return await entry.GetExecuteBeforeBuildAsync().ConfigureAwait(false);
    }

    private static async Task<bool> RunWhenLocalAsync(PlutoniumPipelineContext context, IRunEntryDefinition entry, CancellationToken cancellationToken)
    {
        var apps = entry.RunContext?.Apps;
        if (apps is not null && apps.Count != 0 && apps.Values.All(app => (app.RunType & PlutoniumRunType.Local) == 0))
        {
            return false;
        }

        return await PlutoniumEntryRunner.RunEntryAsync(entry, context.Options, context.Services, cancellationToken).ConfigureAwait(false);
    }
}
