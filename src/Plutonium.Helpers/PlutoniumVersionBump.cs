namespace Plutonium.Helpers;

/// <summary>
/// Version part bumped by one <see cref="PlutoniumVersionBump"/>: major, minor,
/// patch, or the numeric tail of the prerelease label.
/// </summary>
public enum PlutoniumVersionPart
{
    /// <summary>Bump the major component; reset minor, patch, and the prerelease counter.</summary>
    Major = 0,

    /// <summary>Bump the minor component; reset patch and the prerelease counter.</summary>
    Minor = 1,

    /// <summary>Bump the patch component; reset the prerelease counter.</summary>
    Patch = 2,

    /// <summary>Bump the numeric tail of the prerelease label.</summary>
    Prerelease = 3,
}

/// <summary>
/// One parsed bump instruction: which part moves, whether it increments by
/// <see cref="BumpAssign"/> or assigns it absolutely, and the <see cref="Rank"/>
/// used to order multi-part bump strings so prerelease applies before patch
/// before minor before major.
/// </summary>
public sealed class PlutoniumVersionBump
{
    /// <summary>Gets the version part this bump moves. Never null.</summary>
    public PlutoniumVersionPart Part { get; }

    /// <summary>Gets whether the bump increments (<c>+</c>) rather than assigns (<c>&gt;</c>).</summary>
    public bool IsIncrement { get; }

    /// <summary>Gets the amount added or the value assigned. Always positive.</summary>
    public int BumpAssign { get; }

    /// <summary>Gets the ordering rank: higher parts apply later. Higher wins for repeats.</summary>
    public int Rank { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlutoniumVersionBump"/> class.
    /// </summary>
    /// <param name="part">The version part.</param>
    /// <param name="isIncrement">True for increment, false for assign.</param>
    /// <param name="bumpAssign">The amount or value. Must be positive.</param>
    /// <param name="rank">The ordering rank.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bumpAssign"/> is not positive.</exception>
    public PlutoniumVersionBump(PlutoniumVersionPart part, bool isIncrement, int bumpAssign, int rank)
    {
        if (bumpAssign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bumpAssign), bumpAssign, "Bump amount must be positive.");
        }

        Part = part;
        IsIncrement = isIncrement;
        BumpAssign = bumpAssign;
        Rank = rank;
    }
}
