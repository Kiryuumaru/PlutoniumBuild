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
