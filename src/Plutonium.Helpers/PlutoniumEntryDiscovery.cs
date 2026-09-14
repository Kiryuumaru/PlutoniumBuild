using System.Reflection;

namespace Plutonium.Helpers;

/// <summary>
/// Per-AppId grouping of run entries: the definitions touching one application.
/// </summary>
public sealed class PlutoniumAppEntries
{
    /// <summary>Gets the lowercase AppId key. Never null.</summary>
    public string AppId { get; }

    /// <summary>Gets the test entries for this app.</summary>
    public IReadOnlyList<ITestEntryDefinition> TestEntries { get; }

    /// <summary>Gets the build entries for this app.</summary>
    public IReadOnlyList<IBuildEntryDefinition> BuildEntries { get; }

    /// <summary>Gets the publish entries for this app.</summary>
    public IReadOnlyList<IPublishEntryDefinition> PublishEntries { get; }

    /// <summary>Gets every run entry for this app.</summary>
    public IReadOnlyList<IRunEntryDefinition> RunEntries { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumAppEntries"/> class.
    /// </summary>
    /// <param name="appId">The lowercase AppId key. Must not be null or whitespace.</param>
    /// <param name="testEntries">The test entries. Must not be null.</param>
    /// <param name="buildEntries">The build entries. Must not be null.</param>
    /// <param name="publishEntries">The publish entries. Must not be null.</param>
    /// <param name="runEntries">Every run entry. Must not be null.</param>
    public PlutoniumAppEntries(
        string appId,
        IReadOnlyList<ITestEntryDefinition> testEntries,
        IReadOnlyList<IBuildEntryDefinition> buildEntries,
        IReadOnlyList<IPublishEntryDefinition> publishEntries,
        IReadOnlyList<IRunEntryDefinition> runEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(testEntries);
        ArgumentNullException.ThrowIfNull(buildEntries);
        ArgumentNullException.ThrowIfNull(publishEntries);
        ArgumentNullException.ThrowIfNull(runEntries);
        AppId = appId.Trim().ToLowerInvariant();
        TestEntries = testEntries;
        BuildEntries = buildEntries;
        PublishEntries = publishEntries;
        RunEntries = runEntries;
    }
}

/// <summary>
/// Discovered build shape: per-app entry maps plus the workflow configuration.
/// Files created on an app-specific output directory under a build entry
/// propagate to test and publish entries with the same AppId.
/// </summary>
public sealed class PlutoniumAllEntries
{
    /// <summary>Gets the per-app entries keyed by lowercase AppId.</summary>
    public IReadOnlyDictionary<string, PlutoniumAppEntries> AppEntryMap { get; }

    /// <summary>Gets the workflow configuration. Never null.</summary>
    public IWorkflowConfigEntryDefinition WorkflowConfig { get; }

    /// <summary>Gets the test entries keyed by id.</summary>
    public IReadOnlyDictionary<string, ITestEntryDefinition> TestEntries { get; }

    /// <summary>Gets the build entries keyed by id.</summary>
    public IReadOnlyDictionary<string, IBuildEntryDefinition> BuildEntries { get; }

    /// <summary>Gets the publish entries keyed by id.</summary>
    public IReadOnlyDictionary<string, IPublishEntryDefinition> PublishEntries { get; }

    /// <summary>Gets every run entry keyed by id.</summary>
    public IReadOnlyDictionary<string, IRunEntryDefinition> RunEntries { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumAllEntries"/> class.
    /// </summary>
    /// <param name="appEntryMap">The per-app entries. Must not be null.</param>
    /// <param name="workflowConfig">The workflow configuration. Must not be null.</param>
    /// <param name="testEntries">The test entries. Must not be null.</param>
    /// <param name="buildEntries">The build entries. Must not be null.</param>
    /// <param name="publishEntries">The publish entries. Must not be null.</param>
    /// <param name="runEntries">Every run entry. Must not be null.</param>
    public PlutoniumAllEntries(
        IReadOnlyDictionary<string, PlutoniumAppEntries> appEntryMap,
        IWorkflowConfigEntryDefinition workflowConfig,
        IReadOnlyDictionary<string, ITestEntryDefinition> testEntries,
        IReadOnlyDictionary<string, IBuildEntryDefinition> buildEntries,
        IReadOnlyDictionary<string, IPublishEntryDefinition> publishEntries,
        IReadOnlyDictionary<string, IRunEntryDefinition> runEntries)
    {
        ArgumentNullException.ThrowIfNull(appEntryMap);
        ArgumentNullException.ThrowIfNull(workflowConfig);
        ArgumentNullException.ThrowIfNull(testEntries);
        ArgumentNullException.ThrowIfNull(buildEntries);
        ArgumentNullException.ThrowIfNull(publishEntries);
        ArgumentNullException.ThrowIfNull(runEntries);
        AppEntryMap = appEntryMap;
        WorkflowConfig = workflowConfig;
        TestEntries = testEntries;
        BuildEntries = buildEntries;
        PublishEntries = publishEntries;
        RunEntries = runEntries;
    }
}

/// <summary>
/// Entry discovery: reflects <c>TestEntry</c> / <c>BuildEntry</c> /
/// <c>PublishEntry</c> / <c>WorkflowConfigEntry</c> properties on the build
/// instance, invokes each delegate on a fresh definition, requires the workflow
/// config, expands matrices into indexed clones, validates AppIds, and groups
/// per lowercase AppId.
/// </summary>
public static class PlutoniumEntryDiscovery
{
    /// <summary>
    /// Discovers every entry declared on the build instance.
    /// </summary>
    /// <param name="build">The build instance holding entry properties. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The grouped entries. Never null.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no workflow config is declared, an entry has no AppIds, an Id is empty,
    /// ids collide, or a matrix expander returns no entries.
    /// </exception>
    public static async Task<PlutoniumAllEntries> GetAllAsync(object build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        var declared = new List<(IRunEntryDefinition Definition, string PropertyName)>();
        IWorkflowConfigEntryDefinition? workflowConfig = null;

        foreach (var property in build.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.PropertyType == typeof(TestEntry))
            {
                var entry = (TestEntry?)property.GetValue(build);
                ArgumentNullException.ThrowIfNull(entry, property.Name);
                declared.Add((entry(new TestEntryDefinition()), property.Name));
            }
            else if (property.PropertyType == typeof(BuildEntry))
            {
                var entry = (BuildEntry?)property.GetValue(build);
                ArgumentNullException.ThrowIfNull(entry, property.Name);
                declared.Add((entry(new BuildEntryDefinition()), property.Name));
            }
            else if (property.PropertyType == typeof(PublishEntry))
            {
                var entry = (PublishEntry?)property.GetValue(build);
                ArgumentNullException.ThrowIfNull(entry, property.Name);
                declared.Add((entry(new PublishEntryDefinition()), property.Name));
            }
            else if (property.PropertyType == typeof(WorkflowConfigEntry))
            {
                if (property.Name == "WorkflowConfig")
                {
                    var entry = (WorkflowConfigEntry?)property.GetValue(build);
                    ArgumentNullException.ThrowIfNull(entry, property.Name);
                    workflowConfig = entry(new WorkflowConfigEntryDefinition());
                }
            }
        }

        if (workflowConfig is null)
        {
            throw new InvalidOperationException("WorkflowConfigEntry is not defined.");
        }

        var expanded = new List<IRunEntryDefinition>();
        foreach (var (definition, propertyName) in declared)
        {
            await AppendAsync(definition, propertyName, expanded, cancellationToken).ConfigureAwait(false);
        }

        var testEntries = new List<ITestEntryDefinition>();
        var buildEntries = new List<IBuildEntryDefinition>();
        var publishEntries = new List<IPublishEntryDefinition>();
        foreach (var definition in expanded)
        {
            if (definition.AppIds.Count == 0)
            {
                throw new InvalidOperationException($"AppIds for {definition.Id} is empty.");
            }

            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Id cannot be empty.");
            }

            switch (definition)
            {
                case ITestEntryDefinition test:
                    testEntries.Add(test);
                    break;
                case IBuildEntryDefinition buildEntry:
                    buildEntries.Add(buildEntry);
                    break;
                case IPublishEntryDefinition publish:
                    publishEntries.Add(publish);
                    break;
                default:
                    throw new InvalidOperationException($"Entry '{definition.Id}' has an unknown kind.");
            }
        }

        var appTests = new Dictionary<string, List<ITestEntryDefinition>>(StringComparer.Ordinal);
        var appBuilds = new Dictionary<string, List<IBuildEntryDefinition>>(StringComparer.Ordinal);
        var appPublishes = new Dictionary<string, List<IPublishEntryDefinition>>(StringComparer.Ordinal);
        var appRuns = new Dictionary<string, List<IRunEntryDefinition>>(StringComparer.Ordinal);
        Group(testEntries, appTests, appRuns);
        Group(buildEntries, appBuilds, appRuns);
        Group(publishEntries, appPublishes, appRuns);

        var appEntryMap = new Dictionary<string, PlutoniumAppEntries>(StringComparer.Ordinal);
        foreach (var key in appRuns.Keys)
        {
            appTests.TryGetValue(key, out var tests);
            appBuilds.TryGetValue(key, out var builds);
            appPublishes.TryGetValue(key, out var publishes);
            appEntryMap[key] = new PlutoniumAppEntries(
                key,
                tests ?? [],
                builds ?? [],
                publishes ?? [],
                appRuns[key]);
        }

        return new PlutoniumAllEntries(
            appEntryMap,
            workflowConfig,
            ToMap(testEntries, entry => entry.Id),
            ToMap(buildEntries, entry => entry.Id),
            ToMap(publishEntries, entry => entry.Id),
            ToMap(expanded, entry => entry.Id));
    }

    private static async Task AppendAsync(
        IRunEntryDefinition definition,
        string defaultId,
        List<IRunEntryDefinition> sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultId);
        ArgumentNullException.ThrowIfNull(sink);
        if (definition.Matrix.Count == 0)
        {
            if (definition.Id == "_")
            {
                definition.Id = defaultId;
            }

            sink.Add(definition);
            return;
        }

        var index = 0;
        foreach (var expander in definition.Matrix)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clone = definition.Clone();
            clone.Matrix = [];
            var created = await expander(clone).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(created);
            if (created.Length == 0)
            {
                throw new InvalidOperationException($"Matrix expander for '{defaultId}' returned no entries.");
            }

            foreach (var entry in created)
            {
                ArgumentNullException.ThrowIfNull(entry);
                index++;
                await AppendAsync(entry, defaultId + "_" + index, sink, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void Group<TDefinition>(
        IReadOnlyList<TDefinition> definitions,
        Dictionary<string, List<TDefinition>> typed,
        Dictionary<string, List<IRunEntryDefinition>> runs)
        where TDefinition : IRunEntryDefinition
    {
        foreach (var definition in definitions)
        {
            foreach (var appId in definition.AppIds)
            {
                var key = appId.Value.ToLowerInvariant();
                if (!typed.TryGetValue(key, out var typedList))
                {
                    typedList = [];
                    typed[key] = typedList;
                }

                typedList.Add(definition);
                if (!runs.TryGetValue(key, out var runList))
                {
                    runList = [];
                    runs[key] = runList;
                }

                runList.Add(definition);
            }
        }
    }

    private static IReadOnlyDictionary<string, TDefinition> ToMap<TDefinition>(
        IReadOnlyList<TDefinition> definitions,
        Func<TDefinition, string> keySelector)
    {
        var map = new Dictionary<string, TDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var key = keySelector(definition);
            if (!map.TryAdd(key, definition))
            {
                throw new InvalidOperationException($"Duplicate entry id '{key}'.");
            }
        }

        return map;
    }
}

/// <summary>
/// Optional base for build authors: exposes the <c>WorkflowConfig</c> property
/// discovery looks for plus entry delegates. Authors may also declare entries
/// on any plain object without inheriting this base.
/// </summary>
public abstract class PlutoniumBuildBase
{
    /// <summary>
    /// Gets the workflow configuration delegate. Override to configure the pipeline.
    /// </summary>
    protected virtual WorkflowConfigEntry WorkflowConfig => definition => definition;
}
