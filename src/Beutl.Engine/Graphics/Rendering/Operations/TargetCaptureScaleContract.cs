namespace Beutl.Graphics.Rendering;

/// <summary>Declares how a target capture resolves its materialized pixel density.</summary>
public readonly struct TargetCaptureScaleContract
{
    private static readonly object s_preserveTargetSupplyIdentity =
        TargetCaptureScaleContractKind.PreserveTargetSupply;

    private readonly TargetCaptureScaleContractKind _kind;
    private readonly RenderScaleContract _declaredScale;
    private readonly object? _structuralIdentity;

    private TargetCaptureScaleContract(
        TargetCaptureScaleContractKind kind,
        RenderScaleContract declaredScale = default)
    {
        _kind = kind;
        _declaredScale = declaredScale;
        _structuralIdentity = kind == TargetCaptureScaleContractKind.PreserveTargetSupply
            ? s_preserveTargetSupplyIdentity
            : new TargetCaptureScaleStructuralIdentity(kind, declaredScale.StructuralIdentity);
    }

    /// <summary>
    /// Preserves the resolved density of the enclosing root, finite layer, or target-layer scope.
    /// </summary>
    /// <remarks>
    /// This contract remains late-bound while the graph is recorded. The capture materializes at the active target
    /// density during execution, so a denser enclosing scope is not downsampled before downstream consumers run.
    /// </remarks>
    public static TargetCaptureScaleContract PreserveTargetSupply { get; } =
        new(TargetCaptureScaleContractKind.PreserveTargetSupply);

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
            return _structuralIdentity!;
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
