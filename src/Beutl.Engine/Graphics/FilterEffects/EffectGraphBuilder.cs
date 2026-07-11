using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The declarative recording surface an effect appends node descriptors to (feature 004, data-model §1,
/// research D1) — the replacement for the removed imperative context's recording role. It preserves today's
/// append idiom: each primitive appender advances the logical <see cref="Bounds"/> by the node's forward bounds,
/// and the convenience methods construct the same vocabulary (<c>Blur</c>, <c>Saturate</c>, …) as descriptors.
/// The builder never renders or allocates; it produces an <see cref="EffectGraph"/> the render node compiles and
/// executes (A1). Payloads are validated on append so authoring errors surface at describe time, not execute time.
/// </summary>
public sealed class EffectGraphBuilder
{
    private readonly List<EffectNode> _nodes = [];
    private readonly HashSet<IDisposable> _disposables = new(ReferenceEqualityComparer.Instance);
    private int _childScopeDepth;
    private int _currentChildIndex;

    internal EffectGraphBuilder(
        Rect bounds, float outputScale, float workingScale, float maxWorkingScale = float.PositiveInfinity)
    {
        OriginalBounds = bounds;
        Bounds = bounds;
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = maxWorkingScale;
    }

    /// <summary>The current logical bounds, advanced by each appended node's forward bounds.</summary>
    public Rect Bounds { get; private set; }

    /// <summary>The input logical bounds this describe pass started from.</summary>
    public Rect OriginalBounds { get; }

    /// <summary>The render request's output scale <c>s_out</c> (never a ceiling on working scale).</summary>
    public float OutputScale { get; }

    /// <summary>The working density <c>w</c> the render node resolved for this boundary (FR-012); read-only to authors.</summary>
    public float WorkingScale { get; }

    /// <summary>The working-scale ceiling for brushes constructed at describe time; <c>+Inf</c> = no ceiling (delivery).</summary>
    public float MaxWorkingScale { get; }

    /// <summary>Appends a shader node (snippet or whole-source).</summary>
    public EffectGraphBuilder Shader(ShaderNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends a color-filter node (always coordinate-invariant, always fusable).</summary>
    public EffectGraphBuilder ColorFilter(ColorFilterNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends a Skia image-filter node.</summary>
    public EffectGraphBuilder SkiaFilter(SkiaFilterNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends an imperative geometry node (canvas-drawing session, mandatory bounds contract).</summary>
    public EffectGraphBuilder Geometry(GeometryNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>
    /// Appends an imperative geometry node from a raw draw callback, defaulting to the render-time bounds contract
    /// (full-frame, always correct — a script author rarely can declare exact bounds at describe time). The callback
    /// receives a <see cref="GeometrySession"/> over a freshly-cleared pooled output buffer; to keep the pass input
    /// as a passthrough baseline, draw <c>session.Inputs[0]</c> into the canvas first. Pass an explicit
    /// <paramref name="bounds"/> contract when the geometry's extent is known. This is the ergonomic overload C#
    /// script effects author custom drawing through; a compiled effect that knows its bounds uses
    /// <see cref="Geometry(GeometryNodeDescriptor)"/> with an explicit descriptor instead.
    /// </summary>
    public EffectGraphBuilder Geometry(
        Action<GeometrySession> render, BoundsContract? bounds = null, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        return Append(GeometryNodeDescriptor.Create(render, bounds ?? BoundsContract.RenderTime, structuralToken));
    }

    /// <summary>Appends a Vulkan compute node (GLSL pass set, ping-pong/depth, declared no-Vulkan fallback).</summary>
    public EffectGraphBuilder Compute(ComputeNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends a fan-out split node: one input becomes N (or a dynamic count of) branch outputs.</summary>
    public EffectGraphBuilder Split(SplitNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends a fan-in composite node: the current branch set is composited back into one output.</summary>
    public EffectGraphBuilder Composite(CompositeNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>Appends a per-branch nested graph node: the executor re-describes and runs a child graph per branch index.</summary>
    public EffectGraphBuilder NestedGraph(NestedGraphNodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Append(descriptor);
    }

    /// <summary>
    /// Builds a sampler binding — an eager <see cref="ChildBinding"/> for an invariance-safe value lookup (a LUT, a
    /// curve texture) whose shader lives until the frame's plan has executed: the graph disposes it in
    /// <see cref="EffectGraph.Dispose"/>, so it survives a skipped pass and is never leaked. Sampler contents are a
    /// parameter (a swap re-binds without recompiling); the sampler name is structural (A4). A sampler is the one
    /// child form a fusable snippet accepts (it samples by colour, not position).
    /// </summary>
    public ChildBinding Sampler(string name, SKShader shader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shader);
        Track(shader);
        return new ChildBinding(name, shader);
    }

    /// <summary>
    /// Builds a child-shader binding (a whole-source shader's extra input, e.g. a displacement map) whose shader is a
    /// per-frame resource: the graph disposes it in <see cref="EffectGraph.Dispose"/>, so it survives a skipped pass
    /// and is never leaked. The child name is structural; the shader instance is a parameter (A4).
    /// </summary>
    public ChildBinding Child(string name, SKShader shader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shader);
        Track(shader);
        return new ChildBinding(name, shader);
    }

    /// <summary>
    /// Registers any per-frame disposable (a shader, an <see cref="SKImage"/>, an <see cref="SKPicture"/>, an
    /// <see cref="SKRuntimeEffect"/>, …) for graph-scoped disposal: the graph disposes it once the frame's plan has
    /// executed (even if a pass is skipped for an empty ROI). Registering the same instance twice is a no-op, so it
    /// is disposed exactly once. Returns the argument so it composes fluently.
    /// </summary>
    public T Track<T>(T disposable) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(disposable);
        _disposables.Add(disposable);
        return disposable;
    }

    /// <summary>
    /// Brackets the descriptors a single top-level group child appends so the compiler can attribute each pass to a
    /// child index (feature 004, C10 provenance). Only the outermost scope wins: a nested group's own bracketing is
    /// absorbed into the outer child's index, so a group child that is itself a group is one provenance unit. A
    /// custom grouping effect that does not bracket leaves every child at index <c>0</c> (whole-effect provenance),
    /// which is coarser but always correct.
    /// </summary>
    internal ChildScope BeginChildScope(int index)
    {
        if (_childScopeDepth == 0)
            _currentChildIndex = index;
        _childScopeDepth++;
        return new ChildScope(this);
    }

    private void EndChildScope()
    {
        _childScopeDepth--;
        if (_childScopeDepth == 0)
            _currentChildIndex = 0;
    }

    internal readonly struct ChildScope(EffectGraphBuilder builder) : IDisposable
    {
        public void Dispose() => builder.EndChildScope();
    }

    private EffectGraphBuilder Append(EffectNodeDescriptor descriptor)
    {
        Rect input = Bounds;
        Rect output = descriptor.Bounds.IsRenderTimeResolved
            ? Rect.Invalid
            : descriptor.Bounds.TransformBounds(input);
        _nodes.Add(new EffectNode(descriptor, input, output, _currentChildIndex));
        if (!output.IsInvalid)
        {
            Bounds = output;
        }

        return this;
    }

    /// <summary>
    /// Runs <paramref name="append"/> as an isolated describe unit: if it throws, every node it appended is discarded
    /// and the logical <see cref="Bounds"/> is restored, so a failing describe callback (a C# script that throws
    /// mid-run) leaves the shared builder exactly as it found it and the effect degrades to identity rather than
    /// corrupting a chain's graph. Disposables the unit registered via <see cref="Track{T}"/> stay tracked (disposed
    /// with the graph, never leaked); the caught exception is handed to <paramref name="onError"/> to surface.
    /// Returns <see langword="true"/> when the unit completed and <see langword="false"/> when it was rolled back.
    /// </summary>
    internal bool AppendIsolated(Action append, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(append);
        ArgumentNullException.ThrowIfNull(onError);

        int savedNodeCount = _nodes.Count;
        Rect savedBounds = Bounds;
        try
        {
            append();
            return true;
        }
        catch (Exception ex)
        {
            if (_nodes.Count > savedNodeCount)
                _nodes.RemoveRange(savedNodeCount, _nodes.Count - savedNodeCount);
            Bounds = savedBounds;
            onError(ex);
            return false;
        }
    }

    internal EffectGraph Build() => new(_nodes, OriginalBounds, OutputScale, WorkingScale, _disposables);

    // ---- Convenience vocabulary (mirrors the legacy recording context) ---------------------------------

    /// <summary>Appends a Gaussian blur (Skia filter), inflating bounds by <c>sigma × 3</c>.</summary>
    public EffectGraphBuilder Blur(Size sigma)
    {
        if (sigma.Width < 0) sigma = sigma.WithWidth(0);
        if (sigma.Height < 0) sigma = sigma.WithHeight(0);

        var inflate = new Thickness(sigma.Width * 3, sigma.Height * 3);
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            // A zero-sigma blur is a no-op; null is the canonical no-op signal. Returning inner is also safe now (the
            // executor skips disposal on reference equality), but null keeps the pass from advancing its filter chain.
            inner => sigma.Width == 0 && sigma.Height == 0
                ? null
                : SKImageFilter.CreateBlur(sigma.Width, sigma.Height, inner),
            InflateContract(inflate),
            structuralToken: "Blur"));
    }

    /// <summary>
    /// Appends a drop shadow that keeps the source (union of source and shadow bounds). Backward: the requested
    /// output region contains (a) the source drawn as-is — needing the same input region — and (b) the shadow,
    /// whose pixels in <c>r</c> come from input at <c>r − position</c> gathered over the 3σ blur radius; the
    /// required input is therefore <c>r ∪ (r − position).Inflate(3σ)</c>.
    /// </summary>
    public EffectGraphBuilder DropShadow(Point position, Size sigma, Color color)
    {
        var inflate = new Thickness(sigma.Width * 3, sigma.Height * 3);
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateDropShadow(
                (float)position.X, (float)position.Y, sigma.Width, sigma.Height, color.ToSKColor(), inner),
            BoundsContract.Create(
                r => r.Union(r.Translate(position).Inflate(inflate)),
                r => r.Union(r.Translate(-position).Inflate(inflate))),
            structuralToken: "DropShadow"));
    }

    /// <summary>
    /// Appends a drop shadow that replaces the source with only the shadow. Backward: the output is only the
    /// shadow — no union with the requested region — so the required input is exactly the shadow's source region,
    /// <c>(r − position).Inflate(3σ)</c>.
    /// </summary>
    public EffectGraphBuilder DropShadowOnly(Point position, Size sigma, Color color)
    {
        var inflate = new Thickness(sigma.Width * 3, sigma.Height * 3);
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateDropShadowOnly(
                (float)position.X, (float)position.Y, sigma.Width, sigma.Height, color.ToSKColor(), inner),
            BoundsContract.Create(
                r => r.Translate(position).Inflate(inflate),
                r => r.Translate(-position).Inflate(inflate)),
            structuralToken: "DropShadowOnly"));
    }

    /// <summary>Appends a morphological erode.</summary>
    public EffectGraphBuilder Erode(float radiusX, float radiusY)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateErode(radiusX, radiusY, inner),
            // Identity backward is deliberate and valid for erode only: a neighborhood sample falling outside the
            // input region reads transparent, which can only erode edge pixels further — matching the legacy
            // pipeline, where the baked buffer's edge WAS the bounds edge. Do NOT copy this identity onto a
            // filter whose out-of-region samples would ADD content (blur, dilate, shadows) — those must inflate.
            BoundsContract.Create(r => r, r => r),
            structuralToken: "Erode"));
    }

    /// <summary>
    /// Appends a morphological dilate, inflating bounds by the radius. Backward: an output pixel takes the
    /// maximum over the radius neighborhood, so the required input region inflates by the same radius.
    /// </summary>
    public EffectGraphBuilder Dilate(float radiusX, float radiusY)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateDilate(radiusX, radiusY, inner),
            InflateContract(new Thickness(radiusX, radiusY)),
            structuralToken: "Dilate"));
    }

    /// <summary>
    /// Appends a matrix transform (Skia filter), mapping bounds through <paramref name="matrix"/>. Backward maps
    /// the requested output region through the inverse matrix; a non-invertible matrix yields
    /// <see cref="Rect.Invalid"/>, which the resolver treats as "full input bounds" (safe fallback).
    /// </summary>
    public EffectGraphBuilder Transform(Matrix matrix, BitmapInterpolationMode interpolation)
    {
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateMatrix(matrix.ToSKMatrix(), interpolation.ToSKSamplingOptions(), inner),
            BoundsContract.Create(
                r => r.TransformToAABB(matrix),
                r => matrix.TryInvert(out Matrix inverted) ? r.TransformToAABB(inverted) : Rect.Invalid),
            structuralToken: "Transform"));
    }

    /// <summary>Appends a matrix convolution (Skia filter).</summary>
    public EffectGraphBuilder MatrixConvolution(
        PixelSize kernelSize, float[] kernel, float gain, float bias, PixelPoint kernelOffset,
        GradientSpreadMethod spreadMethod, bool convolveAlpha)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        int w = kernelSize.Width - 1;
        int h = kernelSize.Height - 1;
        // Backward: Skia samples input at p + (i, j) − kernelOffset for i ∈ [0, kw), j ∈ [0, kh), so a requested
        // output region needs the input inflated by kernelOffset on the leading edges and by the remaining kernel
        // extent on the trailing edges. Sides clamp at 0 so an offset outside the kernel never deflates the ROI.
        var backwardInflate = new Thickness(
            Math.Max(0, kernelOffset.X),
            Math.Max(0, kernelOffset.Y),
            Math.Max(0, w - kernelOffset.X),
            Math.Max(0, h - kernelOffset.Y));
        // Forward mirrors backward: an input pixel reaches output at p − (i, j) + offset over i ∈ [0, kw), so the
        // output inflates by (w − offsetX) on the leading edges and offset on the trailing edges — the leading/
        // trailing swap of the backward map. Sides clamp at 0 so an offset outside the kernel never deflates.
        var forwardInflate = new Thickness(
            Math.Max(0, w - kernelOffset.X),
            Math.Max(0, h - kernelOffset.Y),
            Math.Max(0, kernelOffset.X),
            Math.Max(0, kernelOffset.Y));
        return SkiaFilter(SkiaFilterNodeDescriptor.Create(
            inner => SKImageFilter.CreateMatrixConvolution(
                kernelSize.ToSKSizeI(), kernel, gain, bias, kernelOffset.ToSKPointI(),
                spreadMethod.ToSKShaderTileMode(), convolveAlpha, inner),
            BoundsContract.Create(
                r => r.Inflate(forwardInflate),
                r => r.Inflate(backwardInflate)),
            structuralToken: "MatrixConvolution"));
    }

    /// <summary>Appends an arbitrary color matrix as a color-filter node.</summary>
    public EffectGraphBuilder ColorMatrix(ColorMatrix matrix)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                matrix.ToArrayForSkia(array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "ColorMatrix"));
    }

    /// <summary>Appends a saturation adjustment as a color-filter node.</summary>
    public EffectGraphBuilder Saturate(float amount)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateSaturateMatrix(amount, array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "Saturate"));
    }

    /// <summary>Appends a hue rotation as a color-filter node.</summary>
    public EffectGraphBuilder HueRotate(float degrees)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateHueRotateMatrix(degrees, array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "HueRotate"));
    }

    /// <summary>Appends a brightness adjustment as a color-filter node.</summary>
    public EffectGraphBuilder Brightness(float amount)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateBrightness(amount, array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "Brightness"));
    }

    /// <summary>Appends Skia's high-contrast color filter.</summary>
    public EffectGraphBuilder HighContrast(bool grayscale, HighContrastInvertStyle invertStyle, float contrast)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () => SKColorFilter.CreateHighContrast(
                grayscale, (SKHighContrastConfigInvertStyle)invertStyle, contrast),
            structuralToken: "HighContrast"));
    }

    /// <summary>Appends a linear-light lighting color matrix (multiply + add).</summary>
    public EffectGraphBuilder Lighting(Color multiply, Color add)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                var mul = multiply.ToLinear();
                var addLinear = add.ToLinear();
                float[] array = new float[20];
                array[0] = mul.X;
                array[6] = mul.Y;
                array[12] = mul.Z;
                array[18] = 1;
                array[4] = addLinear.X;
                array[9] = addLinear.Y;
                array[14] = addLinear.Z;
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "Lighting"));
    }

    /// <summary>Appends Skia's luma-to-color color filter.</summary>
    public EffectGraphBuilder LumaColor()
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () => SKColorFilter.CreateLumaColor(), structuralToken: "LumaColor"));
    }

    /// <summary>Appends a luminance-to-alpha color matrix.</summary>
    public EffectGraphBuilder LuminanceToAlpha()
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () =>
            {
                float[] array = new float[20];
                Graphics.ColorMatrix.CreateLuminanceToAlphaMatrix(array);
                return SKColorFilter.CreateColorMatrix(array);
            },
            structuralToken: "LuminanceToAlpha"));
    }

    /// <summary>Appends a blend-mode color filter against a constant color.</summary>
    public EffectGraphBuilder BlendMode(Color color, BlendMode blendMode)
    {
        return ColorFilter(ColorFilterNodeDescriptor.Create(
            () => SKColorFilter.CreateBlendMode(color.ToSKColor(), (SKBlendMode)blendMode),
            structuralToken: "BlendMode"));
    }

    private static BoundsContract InflateContract(Thickness inflate)
        => BoundsContract.Create(r => r.Inflate(inflate), r => r.Inflate(inflate));
}
