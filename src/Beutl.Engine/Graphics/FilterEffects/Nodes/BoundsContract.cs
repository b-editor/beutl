using System.Reflection;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The forward/backward bounds contract of an effect node (feature 004, data-model §2, research D6).
/// <see cref="TransformBounds"/> is the forward map (output logical bounds from input bounds; drives the
/// builder's <c>Bounds</c> advancement, exactly as today's <c>transformBounds</c> lambdas do) and
/// <see cref="GetRequiredInputBounds"/> is the backward map (the input region a requested output region
/// needs, FR-011). A node that must operate on the complete input uses <see cref="FullFrame"/>;
/// the compiler then disables ROI cropping for that pass.
/// </summary>
/// <remarks>
/// Coordinate-invariant nodes (per-pixel color ops) get <see cref="Identity"/> by construction and never
/// accept a custom contract — that identity is what makes fusion sound. The <see cref="StructuralIdentity"/>
/// token distinguishes one non-invariant contract from another in the <c>StructuralKey</c> without pinning
/// the parameter values a bounds function closes over (an animated blur sigma re-resolves sizes without a
/// recompile, A4).
/// </remarks>
public readonly struct BoundsContract
{
    private readonly Func<Rect, Rect>? _transformBounds;
    private readonly Func<Rect, Rect>? _getRequiredInputBounds;

    private BoundsContract(
        Func<Rect, Rect>? transformBounds,
        Func<Rect, Rect>? getRequiredInputBounds,
        bool requiresFullInput,
        bool isIdentity,
        BoundsStructuralIdentity structuralIdentity)
    {
        _transformBounds = transformBounds;
        _getRequiredInputBounds = getRequiredInputBounds;
        RequiresFullInput = requiresFullInput;
        IsIdentity = isIdentity;
        StructuralIdentity = structuralIdentity;
    }

    /// <summary>The identity contract: forward and backward are both the identity map. Used by every coordinate-invariant node.</summary>
    public static BoundsContract Identity { get; } =
        new(IdentityMap, IdentityMap, requiresFullInput: false, isIdentity: true,
            BoundsStructuralIdentity.Identity);

    /// <summary>
    /// The full-frame contract: output allocation initially matches the complete input bounds and ROI cropping is
    /// disabled. A geometry callback may subsequently shrink or discard that allocation, but cannot grow or move it
    /// outside the allocated input frame.
    /// </summary>
    public static BoundsContract FullFrame { get; } =
        new(IdentityMap, IdentityMap, requiresFullInput: true, isIdentity: false,
            BoundsStructuralIdentity.FullFrame);

    /// <summary>Forward map: output logical bounds from input logical bounds.</summary>
    public Func<Rect, Rect> TransformBounds => _transformBounds ?? (static r => r);

    /// <summary>Backward map: the input region needed to produce a requested output region (FR-011).</summary>
    public Func<Rect, Rect> GetRequiredInputBounds => _getRequiredInputBounds ?? (static r => r);

    /// <summary>True when this node must receive the complete input frame instead of an ROI crop.</summary>
    public bool RequiresFullInput { get; }

    /// <summary>True for the identity contract (coordinate-invariant nodes).</summary>
    public bool IsIdentity { get; }

    /// <summary>
    /// A stable token identifying the <em>shape</em> of this contract for the structural key. Derived from the
    /// bounds delegates' methods, so two contracts built from the same call site (differing only in the
    /// parameter values they close over) share an identity and do not force a recompile.
    /// </summary>
    internal BoundsStructuralIdentity StructuralIdentity { get; }

    // A defaulted struct would silently fall back to identity maps with no structural identity — a growing filter
    // authored with default(BoundsContract) renders clipped with no diagnostic, exactly the misuse the mandatory
    // contract exists to prevent — so descriptor factories surface it at describe time.
    internal void ThrowIfUninitialized(string paramName)
    {
        if (_transformBounds is null)
        {
            throw new ArgumentException(
                "An uninitialized default(BoundsContract) carries no bounds maps and no structural identity; pass "
                + "BoundsContract.Identity, BoundsContract.FullFrame, or BoundsContract.Create(...).",
                paramName);
        }
    }

    /// <summary>
    /// Creates a non-invariant contract from a forward and backward bounds map. The backward map MUST cover
    /// every input texel the node samples for a given output region (A3); validated by debug parity tests.
    /// </summary>
    /// <param name="transformBounds">Forward map (output bounds from input bounds). Must be non-null.</param>
    /// <param name="getRequiredInputBounds">Backward map (required input region for an output region). Must be non-null.</param>
    public static BoundsContract Create(
        Func<Rect, Rect> transformBounds,
        Func<Rect, Rect> getRequiredInputBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);

        var identity = BoundsStructuralIdentity.Create(transformBounds, getRequiredInputBounds);
        return new BoundsContract(
            transformBounds, getRequiredInputBounds, requiresFullInput: false, isIdentity: false, identity);
    }

    private static Rect IdentityMap(Rect rect) => rect;
}

internal readonly record struct BoundsStructuralIdentity(
    BoundsContractKind Kind,
    MethodInfo? TransformMethod,
    MethodInfo? RequiredInputMethod)
{
    public static BoundsStructuralIdentity Identity { get; } =
        new(BoundsContractKind.Identity, null, null);

    public static BoundsStructuralIdentity FullFrame { get; } =
        new(BoundsContractKind.FullFrame, null, null);

    public static BoundsStructuralIdentity Create(
        Func<Rect, Rect> transformBounds,
        Func<Rect, Rect> getRequiredInputBounds)
        => new(
            BoundsContractKind.Custom,
            transformBounds.Method,
            getRequiredInputBounds.Method);
}

internal enum BoundsContractKind
{
    Identity,
    FullFrame,
    Custom,
}
