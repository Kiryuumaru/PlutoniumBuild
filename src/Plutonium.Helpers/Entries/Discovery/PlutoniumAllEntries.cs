using System.Reflection;
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
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Entries.Discovery;
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
