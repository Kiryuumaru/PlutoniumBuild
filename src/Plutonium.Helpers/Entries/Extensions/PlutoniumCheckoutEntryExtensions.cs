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
