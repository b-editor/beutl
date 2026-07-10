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
        float outputScale, float workingScale, float maxWorkingScale, PipelineDiagnostics? diagnostics = null)
    {
        _canvas = canvas;
        Inputs = inputs;
        Bounds = bounds;
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = maxWorkingScale;
        Diagnostics = diagnostics;
    }

    /// <summary>The pass's logical output bounds (position + size in logical units).</summary>
    public Rect Bounds { get; }

    /// <summary>The render request's output scale <c>s_out</c> (never a ceiling on the working scale).</summary>
    public float OutputScale { get; }

    /// <summary>
    /// The pass's OUTPUT density <c>w</c> (device px per logical unit) — the density of the canvas
    /// <see cref="OpenCanvas"/> returns, so it is what the canvas CTM and all output device-space math need
    /// (translations, clip rects, absolute-length pixel parameters multiply by this). It is <b>not</b> the density of
    /// the inputs: an input's own supply density (which a forward-inflated / over-budget carry can drive below
    /// <see cref="WorkingScale"/>) lives on <see cref="EffectInput.Density"/>. Read that for any quantity measured in
    /// input pixels (a snapshot's device-px margins, traced-contour coordinates); bridge an input blit onto this
    /// canvas by <c>WorkingScale / EffectInput.Density</c>.
    /// </summary>
    public float WorkingScale { get; }

    /// <summary>The working-scale ceiling forwarded into brushes/canvases. <c>+Inf</c> = no ceiling (delivery).</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// The owning renderer's effect-pipeline counters (or <see langword="null"/> when unobserved). Forward it into a
    /// <see cref="BrushConstructor"/> built in the callback so a <see cref="DrawableBrush"/> fill's nested render is
    /// observable on <c>IRenderer.Diagnostics</c> (FR-017). This exposes only observation, not target allocation —
    /// the session still owns every buffer decision (a brush's own scratch is deliberately non-pooled, FR-007).
    /// </summary>
    public PipelineDiagnostics? Diagnostics { get; }

    /// <summary>Read-only views of this pass's materialized inputs, in describe order.</summary>
    public IReadOnlyList<EffectInput> Inputs { get; }

    /// <summary>
    /// The canvas over this pass's output buffer, opened (and cleared) by the executor at the output density
    /// (<see cref="ImmediateCanvas.Density"/> equals <see cref="WorkingScale"/>); the executor disposes it when the
    /// callback returns, so the callback draws into it but MUST NOT dispose it.
    /// </summary>
    public ImmediateCanvas OpenCanvas() => _canvas;

    /// <summary>
    /// Renounces this pass's output: the executor releases the pooled output target and the pass produces no
    /// downstream operation (the §C3 empty-output drop rule). A render-time author calls this when it determines,
    /// only inside the callback, that its resolved output is empty — e.g. an <c>AutoClip</c> whose input has no
    /// non-transparent pixels. Any drawing already done on <see cref="OpenCanvas"/> is discarded. Idempotent; once
    /// called the callback MUST NOT draw again.
    /// </summary>
    public void DiscardOutput() => IsOutputDiscarded = true;

    /// <summary>
    /// Whether the callback renounced this pass's output via <see cref="DiscardOutput"/>. The executor reads it
    /// after the callback returns and drops the pass output when set.
    /// </summary>
    internal bool IsOutputDiscarded { get; private set; }
}
