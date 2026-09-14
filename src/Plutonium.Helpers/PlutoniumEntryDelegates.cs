namespace Plutonium.Helpers;

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
