namespace Beutl.Graphics.Rendering;

/// <summary>Declares how a target capture resolves its materialized pixel density.</summary>
public readonly struct TargetCaptureScaleContract
{
    private readonly TargetCaptureScaleContractKind _kind;
    private readonly RenderScaleContract _declaredScale;

    private TargetCaptureScaleContract(
        TargetCaptureScaleContractKind kind,
        RenderScaleContract declaredScale = default)
    {
        _kind = kind;
        _declaredScale = declaredScale;
    }

    /// <summary>
    /// Resolves a concrete output-derived density without observing the enclosing target's density.
    /// </summary>
    public static TargetCaptureScaleContract MaterializeAtWorkingScale { get; } =
        new(
            TargetCaptureScaleContractKind.Declared,
            RenderScaleContract.MaterializeAtWorkingScale);

    /// <summary>
    /// Preserves the resolved density of the enclosing root, finite layer, or target-layer scope.
    /// </summary>
    /// <remarks>
    /// This contract remains late-bound while the graph is recorded. The capture materializes at the active target
    /// density during execution, so a denser enclosing scope is not downsampled before downstream consumers run.
    /// </remarks>
    public static TargetCaptureScaleContract PreserveTargetSupply { get; } =
        new(TargetCaptureScaleContractKind.PreserveTargetSupply);

    /// <summary>Creates a concrete output-derived capture density contract.</summary>
    /// <param name="resolve">
    /// A pure resolver that receives no input supplies and may use output bounds, output scale, and maximum working
    /// scale.
    /// </param>
    /// <param name="structuralKey">An optional immutable key that identifies the resolver's structural behavior.</param>
    /// <returns>A validated target-capture scale contract.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolve"/> is <see langword="null"/>.</exception>
    public static TargetCaptureScaleContract Custom(
        Func<RenderScaleContext, float> resolve,
        object? structuralKey = null)
        => new(
            TargetCaptureScaleContractKind.Declared,
            RenderScaleContract.Custom(resolve, structuralKey));

    internal bool PreservesTargetSupply
    {
        get
        {
            ThrowIfUninitialized();
            return _kind == TargetCaptureScaleContractKind.PreserveTargetSupply;
        }
    }

    internal object StructuralIdentity
    {
        get
        {
            ThrowIfUninitialized();
            return _kind == TargetCaptureScaleContractKind.PreserveTargetSupply
                ? _kind
                : new TargetCaptureScaleStructuralIdentity(_kind, _declaredScale.StructuralIdentity);
        }
    }

    internal EffectiveScale ResolveDeclared(
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ThrowIfUninitialized();
        if (_kind == TargetCaptureScaleContractKind.PreserveTargetSupply)
        {
            throw new InvalidOperationException(
                "A target-supply-preserving capture resolves against its active target during execution.");
        }

        return _declaredScale.Resolve([], outputBounds, outputScale, maxWorkingScale);
    }

    internal void ThrowIfUninitialized(string? parameterName = null)
    {
        if (_kind == TargetCaptureScaleContractKind.Uninitialized)
        {
            if (parameterName is null)
            {
                throw new InvalidOperationException(
                    "default(TargetCaptureScaleContract) is uninitialized; use a named or custom contract.");
            }

            throw new ArgumentException(
                "default(TargetCaptureScaleContract) is uninitialized; use a named or custom contract.",
                parameterName);
        }
    }
}

internal enum TargetCaptureScaleContractKind : byte
{
    Uninitialized,
    Declared,
    PreserveTargetSupply,
}

internal readonly record struct TargetCaptureScaleStructuralIdentity(
    TargetCaptureScaleContractKind Kind,
    object DeclaredScale);
