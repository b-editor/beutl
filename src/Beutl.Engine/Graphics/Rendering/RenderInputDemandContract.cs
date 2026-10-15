namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares how a one-input operation carries a resolved output demand back to its input.
/// </summary>
/// <remarks>
/// The only contract is <see cref="Unchanged"/>, which is also <see langword="default"/>. An operation that
/// resamples its input declares the backward demand through <see cref="RenderScaleContract.MapInputSupply"/>.
/// </remarks>
public readonly struct RenderInputDemandContract
{
    /// <summary>Gets the contract that passes a resolved output demand to the input untouched.</summary>
    public static RenderInputDemandContract Unchanged => default;

    internal bool IsUnchanged => true;

    internal object StructuralIdentity => nameof(Unchanged);

    internal EffectiveScale Resolve(int inputIndex, EffectiveScale outputDemand)
    {
        if (outputDemand.IsUnbounded)
            throw new ArgumentException("Output demand must be concrete.", nameof(outputDemand));

        return outputDemand;
    }
}
