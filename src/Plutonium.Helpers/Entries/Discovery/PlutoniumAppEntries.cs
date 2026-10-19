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
