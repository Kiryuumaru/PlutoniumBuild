using Plutonium.Core;

namespace Plutonium.Helpers;

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

/// <summary>
/// Cache and checkout extensions for run entries. Cache paths opt entries into
/// caching; bump the invalidator to bust. Checkout knobs shape pipeline fetch.
/// </summary>
public static class PlutoniumCacheEntryExtensions
{
    /// <summary>Appends constant cache paths.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cachePaths">The cache paths. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CachePath<TDefinition>(this TDefinition definition, params PlutoniumPath[] cachePaths)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cachePaths);
        foreach (var path in cachePaths)
        {
            ArgumentNullException.ThrowIfNull(path, nameof(cachePaths));
        }

        definition.CachePath.Add(_ => Task.FromResult(cachePaths));
        return definition;
    }

    /// <summary>Appends cache paths from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cachePaths">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CachePath<TDefinition>(this TDefinition definition, Func<PlutoniumPath[]> cachePaths)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cachePaths);
        definition.CachePath.Add(_ => Task.FromResult(cachePaths()));
        return definition;
    }

    /// <summary>Appends cache paths from a synchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cachePaths">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CachePath<TDefinition>(this TDefinition definition, Func<IRunContext, PlutoniumPath[]> cachePaths)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cachePaths);
        definition.CachePath.Add(context => Task.FromResult(cachePaths(context)));
        return definition;
    }

    /// <summary>Appends cache paths from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cachePaths">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CachePath<TDefinition>(this TDefinition definition, Func<Task<PlutoniumPath[]>> cachePaths)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cachePaths);
        definition.CachePath.Add(_ => cachePaths());
        return definition;
    }

    /// <summary>Appends cache paths from an asynchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cachePaths">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CachePath<TDefinition>(this TDefinition definition, Func<IRunContext, Task<PlutoniumPath[]>> cachePaths)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cachePaths);
        definition.CachePath.Add(cachePaths);
        return definition;
    }

    /// <summary>Sets a constant cache invalidator.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cacheInvalidator">The invalidator. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CacheInvalidator<TDefinition>(this TDefinition definition, string cacheInvalidator)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cacheInvalidator);
        definition.CacheInvalidator = _ => Task.FromResult(cacheInvalidator);
        return definition;
    }

    /// <summary>Sets the cache invalidator from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cacheInvalidator">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CacheInvalidator<TDefinition>(this TDefinition definition, Func<string> cacheInvalidator)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cacheInvalidator);
        definition.CacheInvalidator = _ => Task.FromResult(cacheInvalidator());
        return definition;
    }

    /// <summary>Sets the cache invalidator from a synchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cacheInvalidator">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CacheInvalidator<TDefinition>(this TDefinition definition, Func<IRunContext, string> cacheInvalidator)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cacheInvalidator);
        definition.CacheInvalidator = context => Task.FromResult(cacheInvalidator(context));
        return definition;
    }

    /// <summary>Sets the cache invalidator from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cacheInvalidator">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CacheInvalidator<TDefinition>(this TDefinition definition, Func<Task<string>> cacheInvalidator)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cacheInvalidator);
        definition.CacheInvalidator = _ => cacheInvalidator();
        return definition;
    }

    /// <summary>Sets the cache invalidator from an asynchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="cacheInvalidator">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CacheInvalidator<TDefinition>(this TDefinition definition, Func<IRunContext, Task<string>> cacheInvalidator)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cacheInvalidator);
        definition.CacheInvalidator = cacheInvalidator;
        return definition;
    }
}

/// <summary>
/// Checkout extensions for run entries: fetch depth, tag fetch, and submodule mode.
/// </summary>
public static class PlutoniumCheckoutEntryExtensions
{
    /// <summary>Sets a constant checkout fetch depth. 0 fetches full history.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchDepth">The fetch depth.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchDepth<TDefinition>(this TDefinition definition, int checkoutFetchDepth)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.CheckoutFetchDepth = _ => Task.FromResult(checkoutFetchDepth);
        return definition;
    }

    /// <summary>Sets the fetch depth from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchDepth">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchDepth<TDefinition>(this TDefinition definition, Func<int> checkoutFetchDepth)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchDepth);
        definition.CheckoutFetchDepth = _ => Task.FromResult(checkoutFetchDepth());
        return definition;
    }

    /// <summary>Sets the fetch depth from a synchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchDepth">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchDepth<TDefinition>(this TDefinition definition, Func<IRunContext, int> checkoutFetchDepth)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchDepth);
        definition.CheckoutFetchDepth = context => Task.FromResult(checkoutFetchDepth(context));
        return definition;
    }

    /// <summary>Sets the fetch depth from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchDepth">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchDepth<TDefinition>(this TDefinition definition, Func<Task<int>> checkoutFetchDepth)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchDepth);
        definition.CheckoutFetchDepth = _ => checkoutFetchDepth();
        return definition;
    }

    /// <summary>Sets the fetch depth from an asynchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchDepth">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchDepth<TDefinition>(this TDefinition definition, Func<IRunContext, Task<int>> checkoutFetchDepth)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchDepth);
        definition.CheckoutFetchDepth = checkoutFetchDepth;
        return definition;
    }

    /// <summary>Sets whether checkout fetches tags even when a depth is set.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchTags">True to fetch tags.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchTags<TDefinition>(this TDefinition definition, bool checkoutFetchTags)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.CheckoutFetchTags = _ => Task.FromResult(checkoutFetchTags);
        return definition;
    }

    /// <summary>Sets tag fetch from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchTags">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchTags<TDefinition>(this TDefinition definition, Func<bool> checkoutFetchTags)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchTags);
        definition.CheckoutFetchTags = _ => Task.FromResult(checkoutFetchTags());
        return definition;
    }

    /// <summary>Sets tag fetch from a synchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchTags">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchTags<TDefinition>(this TDefinition definition, Func<IRunContext, bool> checkoutFetchTags)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchTags);
        definition.CheckoutFetchTags = context => Task.FromResult(checkoutFetchTags(context));
        return definition;
    }

    /// <summary>Sets tag fetch from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchTags">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchTags<TDefinition>(this TDefinition definition, Func<Task<bool>> checkoutFetchTags)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchTags);
        definition.CheckoutFetchTags = _ => checkoutFetchTags();
        return definition;
    }

    /// <summary>Sets tag fetch from an asynchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutFetchTags">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutFetchTags<TDefinition>(this TDefinition definition, Func<IRunContext, Task<bool>> checkoutFetchTags)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutFetchTags);
        definition.CheckoutFetchTags = checkoutFetchTags;
        return definition;
    }

    /// <summary>Sets a constant submodule checkout mode.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutSubmodules">The submodule mode.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutSubmodules<TDefinition>(this TDefinition definition, PlutoniumSubmoduleCheckoutType checkoutSubmodules)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.CheckoutSubmodules = _ => Task.FromResult(checkoutSubmodules);
        return definition;
    }

    /// <summary>Sets the submodule mode from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutSubmodules">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutSubmodules<TDefinition>(this TDefinition definition, Func<PlutoniumSubmoduleCheckoutType> checkoutSubmodules)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutSubmodules);
        definition.CheckoutSubmodules = _ => Task.FromResult(checkoutSubmodules());
        return definition;
    }

    /// <summary>Sets the submodule mode from a synchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutSubmodules">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutSubmodules<TDefinition>(this TDefinition definition, Func<IRunContext, PlutoniumSubmoduleCheckoutType> checkoutSubmodules)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutSubmodules);
        definition.CheckoutSubmodules = context => Task.FromResult(checkoutSubmodules(context));
        return definition;
    }

    /// <summary>Sets the submodule mode from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutSubmodules">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutSubmodules<TDefinition>(this TDefinition definition, Func<Task<PlutoniumSubmoduleCheckoutType>> checkoutSubmodules)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutSubmodules);
        definition.CheckoutSubmodules = _ => checkoutSubmodules();
        return definition;
    }

    /// <summary>Sets the submodule mode from an asynchronous function over the run context.</summary>
    /// <typeparam name="TDefinition">The entry definition type.</typeparam>
    /// <param name="definition">The entry definition. Must not be null.</param>
    /// <param name="checkoutSubmodules">The provider. Must not be null.</param>
    /// <returns>The entry definition.</returns>
    public static TDefinition CheckoutSubmodules<TDefinition>(this TDefinition definition, Func<IRunContext, Task<PlutoniumSubmoduleCheckoutType>> checkoutSubmodules)
        where TDefinition : IRunEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkoutSubmodules);
        definition.CheckoutSubmodules = checkoutSubmodules;
        return definition;
    }
}

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

/// <summary>Workflow-config extensions: the six pipeline knobs.</summary>
public static class PlutoniumWorkflowConfigEntryExtensions
{
    /// <summary>Sets a constant pipeline display name.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="name">The name. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition Name<TDefinition>(this TDefinition definition, string name)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(name);
        definition.Name = () => Task.FromResult(name);
        return definition;
    }

    /// <summary>Sets the pipeline display name from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="name">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition Name<TDefinition>(this TDefinition definition, Func<string> name)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(name);
        definition.Name = () => Task.FromResult(name());
        return definition;
    }

    /// <summary>Sets the pipeline display name from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="name">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition Name<TDefinition>(this TDefinition definition, Func<Task<string>> name)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(name);
        definition.Name = name;
        return definition;
    }

    /// <summary>Sets a constant pre-setup RunnerOS.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The RunnerOS. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PreSetupRunnerOS<TDefinition>(this TDefinition definition, PlutoniumRunnerOS runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PreSetupRunnerOS = () => Task.FromResult(runnerOS);
        return definition;
    }

    /// <summary>Sets the pre-setup RunnerOS from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PreSetupRunnerOS<TDefinition>(this TDefinition definition, Func<PlutoniumRunnerOS> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PreSetupRunnerOS = () => Task.FromResult(runnerOS());
        return definition;
    }

    /// <summary>Sets the pre-setup RunnerOS from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PreSetupRunnerOS<TDefinition>(this TDefinition definition, Func<Task<PlutoniumRunnerOS>> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PreSetupRunnerOS = runnerOS;
        return definition;
    }

    /// <summary>Sets a constant post-setup RunnerOS.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The RunnerOS. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PostSetupRunnerOS<TDefinition>(this TDefinition definition, PlutoniumRunnerOS runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PostSetupRunnerOS = () => Task.FromResult(runnerOS);
        return definition;
    }

    /// <summary>Sets the post-setup RunnerOS from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PostSetupRunnerOS<TDefinition>(this TDefinition definition, Func<PlutoniumRunnerOS> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PostSetupRunnerOS = () => Task.FromResult(runnerOS());
        return definition;
    }

    /// <summary>Sets the post-setup RunnerOS from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="runnerOS">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition PostSetupRunnerOS<TDefinition>(this TDefinition definition, Func<Task<PlutoniumRunnerOS>> runnerOS)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runnerOS);
        definition.PostSetupRunnerOS = runnerOS;
        return definition;
    }

    /// <summary>Sets whether release-asset hashes are appended to release notes.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="appendReleaseNotesAssetHashes">True to append hashes.</param>
    /// <returns>The definition.</returns>
    public static TDefinition AppendReleaseNotesAssetHashes<TDefinition>(this TDefinition definition, bool appendReleaseNotesAssetHashes)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.AppendReleaseNotesAssetHashes = () => Task.FromResult(appendReleaseNotesAssetHashes);
        return definition;
    }

    /// <summary>Sets asset-hash append from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="appendReleaseNotesAssetHashes">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition AppendReleaseNotesAssetHashes<TDefinition>(this TDefinition definition, Func<bool> appendReleaseNotesAssetHashes)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(appendReleaseNotesAssetHashes);
        definition.AppendReleaseNotesAssetHashes = () => Task.FromResult(appendReleaseNotesAssetHashes());
        return definition;
    }

    /// <summary>Sets asset-hash append from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="appendReleaseNotesAssetHashes">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition AppendReleaseNotesAssetHashes<TDefinition>(this TDefinition definition, Func<Task<bool>> appendReleaseNotesAssetHashes)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(appendReleaseNotesAssetHashes);
        definition.AppendReleaseNotesAssetHashes = appendReleaseNotesAssetHashes;
        return definition;
    }

    /// <summary>Sets whether off-main releases are marked prerelease.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="enablePrereleaseOnRelease">True to enable prereleases.</param>
    /// <returns>The definition.</returns>
    public static TDefinition EnablePrereleaseOnRelease<TDefinition>(this TDefinition definition, bool enablePrereleaseOnRelease)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.EnablePrereleaseOnRelease = () => Task.FromResult(enablePrereleaseOnRelease);
        return definition;
    }

    /// <summary>Sets prerelease-on-release from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="enablePrereleaseOnRelease">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition EnablePrereleaseOnRelease<TDefinition>(this TDefinition definition, Func<bool> enablePrereleaseOnRelease)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(enablePrereleaseOnRelease);
        definition.EnablePrereleaseOnRelease = () => Task.FromResult(enablePrereleaseOnRelease());
        return definition;
    }

    /// <summary>Sets prerelease-on-release from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="enablePrereleaseOnRelease">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition EnablePrereleaseOnRelease<TDefinition>(this TDefinition definition, Func<Task<bool>> enablePrereleaseOnRelease)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(enablePrereleaseOnRelease);
        definition.EnablePrereleaseOnRelease = enablePrereleaseOnRelease;
        return definition;
    }

    /// <summary>Sets whether JSON file versioning is used.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="useJsonFileVersioning">True to use JSON versioning.</param>
    /// <returns>The definition.</returns>
    public static TDefinition UseJsonFileVersioning<TDefinition>(this TDefinition definition, bool useJsonFileVersioning)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.UseJsonFileVersioning = () => Task.FromResult(useJsonFileVersioning);
        return definition;
    }

    /// <summary>Sets JSON file versioning from a synchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="useJsonFileVersioning">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition UseJsonFileVersioning<TDefinition>(this TDefinition definition, Func<bool> useJsonFileVersioning)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(useJsonFileVersioning);
        definition.UseJsonFileVersioning = () => Task.FromResult(useJsonFileVersioning());
        return definition;
    }

    /// <summary>Sets JSON file versioning from an asynchronous function.</summary>
    /// <typeparam name="TDefinition">The workflow-config definition type.</typeparam>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <param name="useJsonFileVersioning">The provider. Must not be null.</param>
    /// <returns>The definition.</returns>
    public static TDefinition UseJsonFileVersioning<TDefinition>(this TDefinition definition, Func<Task<bool>> useJsonFileVersioning)
        where TDefinition : IWorkflowConfigEntryDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(useJsonFileVersioning);
        definition.UseJsonFileVersioning = useJsonFileVersioning;
        return definition;
    }
}
