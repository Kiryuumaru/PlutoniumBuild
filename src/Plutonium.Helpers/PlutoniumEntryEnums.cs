namespace Plutonium.Helpers;

/// <summary>
/// Kind of run entry. Drives per-app grouping and workflow fan-out.
/// </summary>
public enum PlutoniumEntryType
{
    /// <summary>Test entry.</summary>
    Test = 0,

    /// <summary>Build entry.</summary>
    Build = 1,

    /// <summary>Publish entry.</summary>
    Publish = 2,
}

/// <summary>
/// How pipeline checkout handles git submodules for an entry.
/// </summary>
public enum PlutoniumSubmoduleCheckoutType
{
    /// <summary>Do not fetch submodules.</summary>
    None = 0,

    /// <summary>Checkout a single level of submodules.</summary>
    SingleLevel = 1,

    /// <summary>Checkout submodules of submodules.</summary>
    Recursive = 2,
}
