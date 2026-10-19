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
/// Fluent entry-definition extensions: AppId, condition, RunnerOS, execute steps,
/// cache, checkout, workflow display, matrix fan-out, and test ordering.
/// Each overload set mirrors sync/async with and without the run context.
/// </summary>
public static class PlutoniumRunEntryExtensions
{
    /// <summary>Sets the single AppId for the entry.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="appId">The application id. Must satisfy the AppId rule.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition AppId<TDefinition>(this TDefinition definition, string appId)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.AppIds = [new PlutoniumAppId(appId)];
        return definition;
    }

    /// <summary>Sets the AppIds for the entry.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="appIds">The application ids. Each must satisfy the AppId rule.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition AppId<TDefinition>(this TDefinition definition, params string[] appIds)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(appIds);
        foreach (var id in appIds)
        {
            ArgumentNullException.ThrowIfNull(id, nameof(appIds));
        }

        definition.AppIds = appIds.Select(id => new PlutoniumAppId(id)).ToList();
        return definition;
    }

    /// <summary>Sets the AppIds for the entry from validated identifiers.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="appIds">The application ids. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition AppId<TDefinition>(this TDefinition definition, params PlutoniumAppId[] appIds)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(appIds);
        foreach (var id in appIds)
        {
            ArgumentNullException.ThrowIfNull(id, nameof(appIds));
        }

        definition.AppIds = [.. appIds];
        return definition;
    }

    /// <summary>Sets a constant entry condition.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="condition">The condition.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Condition<TDefinition>(this TDefinition definition, bool condition)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Condition = _ => Task.FromResult(condition);
        return definition;
    }

    /// <summary>Sets the entry condition from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="condition">The condition function. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Condition<TDefinition>(this TDefinition definition, Func<bool> condition)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(condition);
        definition.Condition = _ => Task.FromResult(condition());
        return definition;
    }

    /// <summary>Sets the entry condition from a synchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="condition">The condition function. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Condition<TDefinition>(this TDefinition definition, Func<IRunContext, bool> condition)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(condition);
        definition.Condition = context => Task.FromResult(condition(context));
        return definition;
    }

    /// <summary>Sets the entry condition from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="condition">The condition function. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Condition<TDefinition>(this TDefinition definition, Func<Task<bool>> condition)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(condition);
        definition.Condition = _ => condition();
        return definition;
    }

    /// <summary>Sets the entry condition from an asynchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="condition">The condition function. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Condition<TDefinition>(this TDefinition definition, Func<IRunContext, Task<bool>> condition)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(condition);
        definition.Condition = condition;
        return definition;
    }

    /// <summary>Sets a constant RunnerOS for the entry.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="runnerOS">The RunnerOS. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition RunnerOS<TDefinition>(this TDefinition definition, PlutoniumRunnerOS runnerOS)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.RunnerOS = _ => Task.FromResult(runnerOS);
        return definition;
    }

    /// <summary>Sets the RunnerOS from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="runnerOS">The selector. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition RunnerOS<TDefinition>(this TDefinition definition, Func<PlutoniumRunnerOS> runnerOS)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.RunnerOS = _ => Task.FromResult(runnerOS());
        return definition;
    }

    /// <summary>Sets the RunnerOS from a synchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="runnerOS">The selector. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition RunnerOS<TDefinition>(this TDefinition definition, Func<IRunContext, PlutoniumRunnerOS> runnerOS)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.RunnerOS = context => Task.FromResult(runnerOS(context));
        return definition;
    }

    /// <summary>Sets the RunnerOS from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="runnerOS">The selector. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition RunnerOS<TDefinition>(this TDefinition definition, Func<Task<PlutoniumRunnerOS>> runnerOS)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.RunnerOS = _ => runnerOS();
        return definition;
    }

    /// <summary>Sets the RunnerOS from an asynchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="runnerOS">The selector. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition RunnerOS<TDefinition>(this TDefinition definition, Func<IRunContext, Task<PlutoniumRunnerOS>> runnerOS)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.RunnerOS = runnerOS;
        return definition;
    }

    /// <summary>Appends a synchronous execute step.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="action">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Execute<TDefinition>(this TDefinition definition, Action action)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(action);
        definition.Execute.Add(_ =>
        {
            action();
            return Task.CompletedTask;
        });
        return definition;
    }

    /// <summary>Appends a synchronous execute step with a discarded return value.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <typeparam name="T">The discarded return type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="action">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Execute<TDefinition, T>(this TDefinition definition, Func<T> action)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(action);
        definition.Execute.Add(_ =>
        {
            action();
            return Task.CompletedTask;
        });
        return definition;
    }

    /// <summary>Appends an asynchronous execute step.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="action">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Execute<TDefinition>(this TDefinition definition, Func<Task> action)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(action);
        definition.Execute.Add(_ => action());
        return definition;
    }

    /// <summary>Appends an asynchronous execute step with a discarded return value.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <typeparam name="T">The discarded return type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="action">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Execute<TDefinition, T>(this TDefinition definition, Func<Task<T>> action)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(action);
        definition.Execute.Add(async _ => await action().ConfigureAwait(false));
        return definition;
    }

    /// <summary>Appends a synchronous execute step over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="action">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Execute<TDefinition>(this TDefinition definition, Action<IRunContext> action)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(action);
        definition.Execute.Add(context =>
        {
            action(context);
            return Task.CompletedTask;
        });
        return definition;
    }

    /// <summary>Appends an asynchronous execute step over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="action">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Execute<TDefinition>(this TDefinition definition, Func<IRunContext, Task> action)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(action);
        definition.Execute.Add(action);
        return definition;
    }

    /// <summary>Appends an asynchronous execute step over the run context with a discarded return value.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <typeparam name="T">The discarded return type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="action">The step. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition Execute<TDefinition, T>(this TDefinition definition, Func<IRunContext, Task<T>> action)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(action);
        definition.Execute.Add(async context => await action(context).ConfigureAwait(false));
        return definition;
    }
}
