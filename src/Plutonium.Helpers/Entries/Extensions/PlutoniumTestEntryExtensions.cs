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
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Entries.Extensions;
/// <summary>Test-entry extensions: whether the test orders before the build within an app.</summary>
public static class PlutoniumTestEntryExtensions
{
    /// <summary>Sets a constant execute-before-build flag.</summary>
    /// <typeparam name="TDefinition">The test entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="executeBeforeBuild">True to order the test before the build.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition ExecuteBeforeBuild<TDefinition>(this TDefinition definition, bool executeBeforeBuild)
        where TDefinition : ITestEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.ExecuteBeforeBuild = () => Task.FromResult(executeBeforeBuild);
        return definition;
    }

    /// <summary>Sets execute-before-build from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The test entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="executeBeforeBuild">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition ExecuteBeforeBuild<TDefinition>(this TDefinition definition, Func<bool> executeBeforeBuild)
        where TDefinition : ITestEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(executeBeforeBuild);
        definition.ExecuteBeforeBuild = () => Task.FromResult(executeBeforeBuild());
        return definition;
    }

    /// <summary>Sets execute-before-build from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The test entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="executeBeforeBuild">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition ExecuteBeforeBuild<TDefinition>(this TDefinition definition, Func<Task<bool>> executeBeforeBuild)
        where TDefinition : ITestEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(executeBeforeBuild);
        definition.ExecuteBeforeBuild = executeBeforeBuild;
        return definition;
    }
}
