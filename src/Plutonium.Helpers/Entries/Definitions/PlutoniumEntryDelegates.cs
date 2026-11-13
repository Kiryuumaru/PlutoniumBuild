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

/// <summary>
/// Delegate declaring a test entry. Receives a test definition and returns it after configuration.
/// </summary>
/// <param name="definition">The test entry definition. Never null.</param>
/// <returns>The configured test entry definition. Never null.</returns>
public delegate ITestEntryDefinition TestEntry(ITestEntryDefinition definition);

/// <summary>
/// Delegate declaring a build entry. Receives a build definition and returns it after configuration.
/// </summary>
/// <param name="definition">The build entry definition. Never null.</param>
/// <returns>The configured build entry definition. Never null.</returns>
public delegate IBuildEntryDefinition BuildEntry(IBuildEntryDefinition definition);

/// <summary>
/// Delegate declaring a publish entry. Receives a publish definition and returns it after configuration.
/// </summary>
/// <param name="definition">The publish entry definition. Never null.</param>
/// <returns>The configured publish entry definition. Never null.</returns>
public delegate IPublishEntryDefinition PublishEntry(IPublishEntryDefinition definition);

/// <summary>
/// Delegate declaring the workflow configuration. Receives a workflow-config definition and returns it after configuration.
/// </summary>
/// <param name="definition">The workflow-config entry definition. Never null.</param>
/// <returns>The configured workflow-config entry definition. Never null.</returns>
public delegate IWorkflowConfigEntryDefinition WorkflowConfigEntry(IWorkflowConfigEntryDefinition definition);
