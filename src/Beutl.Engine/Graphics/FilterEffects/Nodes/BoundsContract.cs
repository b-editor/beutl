namespace Beutl.Graphics.Effects;

/// <summary>
/// The forward/backward bounds contract of an effect node (feature 004, data-model §2, research D6).
/// <see cref="TransformBounds"/> is the forward map (output logical bounds from input bounds; drives the
/// builder's <c>Bounds</c> advancement, exactly as today's <c>transformBounds</c> lambdas do) and
/// <see cref="GetRequiredInputBounds"/> is the backward map (the input region a requested output region
/// needs, FR-011). A node that cannot answer either until execution sets <see cref="IsRenderTimeResolved"/>;
/// the compiler then falls back to the full input bounds for the ROI (safe degradation).
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
        bool isRenderTimeResolved,
        bool isIdentity,
        long structuralIdentity)
    {
        _transformBounds = transformBounds;
        _getRequiredInputBounds = getRequiredInputBounds;
        IsRenderTimeResolved = isRenderTimeResolved;
        IsIdentity = isIdentity;
        StructuralIdentity = structuralIdentity;
    }

    /// <summary>The identity contract: forward and backward are both the identity map. Used by every coordinate-invariant node.</summary>
    public static BoundsContract Identity { get; } =
        new(static r => r, static r => r, isRenderTimeResolved: false, isIdentity: true, structuralIdentity: 1);

    /// <summary>
    /// The render-time contract: the forward map returns <see cref="Rect.Invalid"/> and the compiler defers
    /// layout to execution, using the full input bounds for the ROI. Backward is the identity.
    /// </summary>
    public static BoundsContract RenderTime { get; } =
        new(static _ => Rect.Invalid, static r => r, isRenderTimeResolved: true, isIdentity: false, structuralIdentity: 2);

    /// <summary>Forward map: output logical bounds from input logical bounds.</summary>
    public Func<Rect, Rect> TransformBounds => _transformBounds ?? (static r => r);

    /// <summary>Backward map: the input region needed to produce a requested output region (FR-011).</summary>
    public Func<Rect, Rect> GetRequiredInputBounds => _getRequiredInputBounds ?? (static r => r);

    /// <summary>True when this node cannot compute bounds until execution; the compiler falls back to full input bounds for the ROI.</summary>
    public bool IsRenderTimeResolved { get; }

    /// <summary>True for the identity contract (coordinate-invariant nodes).</summary>
    public bool IsIdentity { get; }

    /// <summary>
    /// A stable token identifying the <em>shape</em> of this contract for the structural key. Derived from the
    /// bounds delegates' methods, so two contracts built from the same call site (differing only in the
    /// parameter values they close over) share an identity and do not force a recompile.
    /// </summary>
    internal long StructuralIdentity { get; }

    /// <summary>
    /// Creates a non-invariant contract from a forward and backward bounds map. The backward map MUST cover
    /// every input texel the node samples for a given output region (A3); validated by debug parity tests.
    /// </summary>
    /// <param name="transformBounds">Forward map (output bounds from input bounds). Must be non-null.</param>
    /// <param name="getRequiredInputBounds">Backward map (required input region for an output region). Must be non-null.</param>
    /// <param name="isRenderTimeResolved">When true, layout is deferred to execution and the ROI falls back to full input bounds.</param>
    public static BoundsContract Create(
        Func<Rect, Rect> transformBounds,
        Func<Rect, Rect> getRequiredInputBounds,
        bool isRenderTimeResolved = false)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);

        long identity = DeriveIdentity(transformBounds, getRequiredInputBounds, isRenderTimeResolved);
        return new BoundsContract(transformBounds, getRequiredInputBounds, isRenderTimeResolved, isIdentity: false, identity);
    }

    private static long DeriveIdentity(
        Func<Rect, Rect> transformBounds, Func<Rect, Rect> getRequiredInputBounds, bool isRenderTimeResolved)
    {
        var hash = new HashCode();
        hash.Add(transformBounds.Method.MethodHandle.Value);
        hash.Add(getRequiredInputBounds.Method.MethodHandle.Value);
        hash.Add(isRenderTimeResolved);
        // Keep it non-zero and distinct from the two reserved Identity/RenderTime tokens.
        return ((long)hash.ToHashCode() << 2) | 3;
    }
}
