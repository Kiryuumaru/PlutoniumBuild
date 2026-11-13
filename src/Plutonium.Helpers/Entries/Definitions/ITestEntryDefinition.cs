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
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;
using Plutonium.Helpers.Runtime;
using Plutonium.Helpers.Pipelines;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Helpers.Pipelines.Models;
using Plutonium.Helpers.Pipelines.RunnerOS;
using Plutonium.Helpers.Versioning;
using Plutonium.Helpers.App;

namespace Plutonium.Helpers.Entries.Definitions;
/// <summary>Test entry definition.</summary>
public interface ITestEntryDefinition : IDependentEntryDefinition
{
    /// <summary>Gets or sets whether the test runs before the build within an app. Defaults to false.</summary>
    internal Func<Task<bool>> ExecuteBeforeBuild { get; set; }

    /// <summary>Resolves whether the test runs before the build.</summary>
    /// <returns>True when the test orders before the build.</returns>
    Task<bool> GetExecuteBeforeBuildAsync();
}
