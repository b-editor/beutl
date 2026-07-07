using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The bounded escape hatch a <see cref="GeometryNodeDescriptor"/> callback draws through (feature 004,
/// data-model §1, research D8) — the replacement for the removed imperative custom-effect context's authoring role.
/// It exposes the pass's resolved scales, read-only views of its inputs, and a single canvas over the
/// executor-acquired pooled output target. It deliberately exposes <b>no</b> target creation, flushing, or
/// output snapshot: every resource and synchronization decision stays with the executor, which brackets the
/// canvas lifecycle (a session cannot outlive its pass). Multi-output needs (fan-out) are expressed as a
/// <see cref="SplitNodeDescriptor"/>, not by allocating extra targets here.
/// </summary>
public sealed class GeometrySession
{
    private readonly ImmediateCanvas _canvas;

    internal GeometrySession(
        ImmediateCanvas canvas, IReadOnlyList<EffectInput> inputs, Rect bounds,
        float outputScale, float workingScale, float maxWorkingScale)
    {
        _canvas = canvas;
        Inputs = inputs;
        Bounds = bounds;
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = maxWorkingScale;
    }

    /// <summary>The pass's logical output bounds (position + size in logical units).</summary>
    public Rect Bounds { get; }

    /// <summary>The render request's output scale <c>s_out</c> (never a ceiling on the working scale).</summary>
    public float OutputScale { get; }

    /// <summary>The working density <c>w</c> resolved for this pass's inputs (FR-012); absolute-length pixel parameters multiply by this.</summary>
    public float WorkingScale { get; }

    /// <summary>The working-scale ceiling forwarded into brushes/canvases. <c>+Inf</c> = no ceiling (delivery).</summary>
    public float MaxWorkingScale { get; }

    /// <summary>Read-only views of this pass's materialized inputs, in describe order.</summary>
    public IReadOnlyList<EffectInput> Inputs { get; }

    /// <summary>
    /// The canvas over this pass's output buffer. The executor opened it (cleared, at the output buffer's
    /// device density, which may be clamped below <see cref="WorkingScale"/> — read <see cref="ImmediateCanvas.Density"/>
    /// for the real density) and disposes it when the callback returns; the callback draws into it but MUST NOT
    /// dispose it.
    /// </summary>
    public ImmediateCanvas OpenCanvas() => _canvas;
}
