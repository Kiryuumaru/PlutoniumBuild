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
/// <summary>
/// Workflow-display extensions: workflow id, workflow builder steps, display name,
/// and matrix fan-out. These affect generated workflow YAML, not local runs.
/// </summary>
public static class PlutoniumWorkflowEntryExtensions
{
    /// <summary>Sets the workflow id (the entry <c>Id</c>) for workflow fan-out.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="workflowId">The workflow id. Must not be null or whitespace.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition WorkflowId<TDefinition>(this TDefinition definition, string workflowId)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        definition.Id = workflowId.Trim();
        return definition;
    }

    /// <summary>Appends a synchronous workflow-builder step.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="workflowBuilder">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition WorkflowBuilder<TDefinition>(this TDefinition definition, Action<IPlutoniumWorkflowBuilder> workflowBuilder)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(workflowBuilder);
        definition.WorkflowBuilder.Add(builder =>
        {
            workflowBuilder(builder);
            return Task.CompletedTask;
        });
        return definition;
    }

    /// <summary>Appends an asynchronous workflow-builder step.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="workflowBuilder">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition WorkflowBuilder<TDefinition>(this TDefinition definition, Func<IPlutoniumWorkflowBuilder, Task> workflowBuilder)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(workflowBuilder);
        definition.WorkflowBuilder.Add(workflowBuilder);
        return definition;
    }

    /// <summary>Appends an asynchronous workflow-builder step with a discarded return value.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <typeparam name="T">The discarded return type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="workflowBuilder">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition WorkflowBuilder<TDefinition, T>(this TDefinition definition, Func<IPlutoniumWorkflowBuilder, Task<T>> workflowBuilder)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(workflowBuilder);
        definition.WorkflowBuilder.Add(async builder => await workflowBuilder(builder).ConfigureAwait(false));
        return definition;
    }

    /// <summary>Sets a constant display name.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="displayName">The display name. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition DisplayName<TDefinition>(this TDefinition definition, string displayName)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(displayName);
        definition.DisplayName = _ => Task.FromResult(displayName);
        return definition;
    }

    /// <summary>Sets the display name from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="displayName">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition DisplayName<TDefinition>(this TDefinition definition, Func<string> displayName)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(displayName);
        definition.DisplayName = _ => Task.FromResult(displayName());
        return definition;
    }

    /// <summary>Sets the display name from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="displayName">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition DisplayName<TDefinition>(this TDefinition definition, Func<Task<string>> displayName)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(displayName);
        definition.DisplayName = _ => displayName();
        return definition;
    }

    /// <summary>
    /// Fans one declared entry into one clone per matrix value. Discovery expands
    /// each clone into its own entry suffixed with the one-based index.
    /// The suffix is <c>defaultId_index</c> with a one-based index shared across
    /// expanders, so ids stay unique and ordered.
    /// </summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <typeparam name="TMatrix">The matrix value type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="matrix">The matrix values. Must not be null.</param>
    /// <param name="matrixDefinition">The per-value configuration. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Matrix<TDefinition, TMatrix>(
        this TDefinition definition,
        TMatrix[] matrix,
        Action<TDefinition, TMatrix> matrixDefinition)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(matrixDefinition);
        foreach (var value in matrix)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(matrix));
        }

        definition.Matrix.Add(clone =>
        {
            var expanded = new List<IRunEntryDefinition>();
            foreach (var value in matrix)
            {
                var subClone = (TDefinition)clone.Clone();
                matrixDefinition(subClone, value);
                expanded.Add(subClone);
            }

            return Task.FromResult<IRunEntryDefinition[]>([.. expanded]);
        });
        return definition;
    }
}
