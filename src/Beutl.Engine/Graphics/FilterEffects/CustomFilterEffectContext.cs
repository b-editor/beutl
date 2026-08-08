using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    private static readonly ILogger s_logger = Log.CreateLogger("CustomFilterEffectContext");
    private readonly Vector _deviceGridOffset;
    private readonly IReadOnlyDictionary<FilterEffectBrush, LoweredBrush>? _brushes;

    internal CustomFilterEffectContext(
        EffectTargets targets,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        Vector? deviceGridOffset = null,
        IReadOnlyDictionary<FilterEffectBrush, LoweredBrush>? brushes = null)
    {
        _brushes = brushes;
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");

        Targets = targets;
        _deviceGridOffset = deviceGridOffset
            ?? (targets.Count > 0 ? targets[0].DeviceGridOffset : default);
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        Intent = intent;
        Purpose = purpose;
    }

    public EffectTargets Targets { get; }

    /// <summary>
    /// Creates the Skia shader for a brush registered through <see cref="FilterEffectContext.RegisterBrush"/>.
    /// </summary>
    /// <param name="brush">The non-null handle returned while recording this effect.</param>
    /// <param name="bounds">The logical frame the brush maps onto.</param>
    /// <param name="blendMode">The blend mode to configure on the produced shader.</param>
    /// <param name="scale">The device density of the buffer being painted.</param>
    /// <returns>The caller-owned shader, or <see langword="null"/> when the brush produces no shader.</returns>
    public SKShader? CreateBrushShader(
        FilterEffectBrush brush,
        Rect bounds,
        BlendMode blendMode,
        float scale)
        => CreateBrushConstructor(brush, bounds, blendMode, scale).CreateShader();

    /// <summary>Configures a paint from a brush registered through <see cref="FilterEffectContext.RegisterBrush"/>.</summary>
    /// <param name="paint">The non-null paint to configure.</param>
    /// <param name="brush">The non-null handle returned while recording this effect.</param>
    /// <param name="bounds">The logical frame the brush maps onto.</param>
    /// <param name="blendMode">The blend mode to configure on the paint.</param>
    /// <param name="scale">The device density of the buffer being painted.</param>
    public void ConfigureBrushPaint(
        SKPaint paint,
        FilterEffectBrush brush,
        Rect bounds,
        BlendMode blendMode,
        float scale)
    {
        ArgumentNullException.ThrowIfNull(paint);
        CreateBrushConstructor(brush, bounds, blendMode, scale).ConfigurePaint(paint);
    }

    /// <summary>
    /// Draws a Skia path with a fill and pen registered through
    /// <see cref="FilterEffectContext.RegisterBrush"/> / <see cref="FilterEffectContext.RegisterPen"/>.
    /// </summary>
    /// <param name="canvas">The non-null canvas opened for the target being painted.</param>
    /// <param name="path">The non-null path in the canvas's current coordinate space.</param>
    /// <param name="strokeOnly">Whether to skip the fill and stroke only.</param>
    /// <param name="fill">The fill handle; pass <see cref="FilterEffectBrush.Empty"/> for no fill.</param>
    /// <param name="pen">The pen handle; pass <see cref="FilterEffectPen.Empty"/> for no stroke.</param>
    public void DrawPath(
        ImmediateCanvas canvas,
        SKPath path,
        bool strokeOnly,
        FilterEffectBrush fill,
        FilterEffectPen pen)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(fill);
        ArgumentNullException.ThrowIfNull(pen);
        canvas.DrawSKPath(path, strokeOnly, Resolve(fill), new LoweredPen(null, pen.Resource, Resolve(pen.Brush)));
    }

    /// <summary>
    /// Creates an activator for a nested effect re-application that inherits this callback's execution settings
    /// and its resolved brushes.
    /// </summary>
    /// <param name="targets">The non-null targets the nested re-application runs over.</param>
    /// <param name="builder">The non-null caller-owned Skia filter builder for the nested run.</param>
    /// <returns>A caller-owned activator.</returns>
    /// <remarks>
    /// A nested re-application records against a standalone <see cref="FilterEffectContext"/>, which cannot lower
    /// nested <see cref="DrawableBrush"/> content. Lower it while recording with
    /// <see cref="FilterEffectContext.LowerNestedEffectBrushes"/>; the activator this method returns is what
    /// resolves those handles again during the nested run.
    /// </remarks>
    public FilterEffectActivator CreateNestedActivator(EffectTargets targets, SKImageFilterBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(builder);
        return new FilterEffectActivator(
            targets,
            builder,
            Intent,
            Purpose,
            OutputScale,
            WorkingScale,
            MaxWorkingScale,
            _brushes);
    }

    private LoweredBrush Resolve(FilterEffectBrush brush)
        => _brushes is not null && _brushes.TryGetValue(brush, out LoweredBrush resolved)
            ? resolved
            : new LoweredBrush(null, brush.Resource, null);

    private BrushConstructor CreateBrushConstructor(
        FilterEffectBrush brush,
        Rect bounds,
        BlendMode blendMode,
        float scale)
    {
        ArgumentNullException.ThrowIfNull(brush);
        return new BrushConstructor(bounds, Resolve(brush), blendMode, scale, MaxWorkingScale, Intent);
    }

    /// <summary>The render request's output scale <c>s_out</c>, not a ceiling on this effect's working scale.</summary>
    public float OutputScale { get; }

    /// <summary>
    /// Gets the nominal working density <c>w</c> requested for this callback. <see cref="CreateTarget"/>
    /// can clamp a specific allocation below this value; call <see cref="ResolveTargetDensity"/> before
    /// allocation or use the returned target's <see cref="EffectTarget.Scale"/> for device-pixel math.
    /// </summary>
    public float WorkingScale { get; }

    /// <summary>Working-scale ceiling forwarded into canvases from <see cref="Open"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// Gets the translation from effect-local coordinates to the composition-device grid used
    /// for intermediate allocation.
    /// </summary>
    public Vector DeviceGridOffset => _deviceGridOffset;

    /// <summary>Gets the explicit preview or delivery classification for this execution.</summary>
    public RenderIntent Intent { get; }

    /// <summary>Gets the explicit request purpose for this execution.</summary>
    public RenderRequestPurpose Purpose { get; }

    public void ForEach(Action<int, EffectTarget> action)
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            EffectTarget target = Targets[i];
            action(i, target);
        }
    }

    public void ForEach(Func<int, EffectTarget, EffectTarget> action)
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            EffectTarget target = Targets[i];
            EffectTarget newTarget = action(i, target);
            if (newTarget != target)
            {
                target.Dispose();
                Targets[i] = newTarget;
            }
        }
    }

    public void ForEach(Func<int, EffectTarget, EffectTargets> action)
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            using EffectTarget target = Targets[i];
            EffectTargets newTargets = action(i, target.Clone());

            Targets.RemoveAt(i);
            Targets.InsertRange(i, newTargets);
            i += newTargets.Count - 1;
        }
    }

    /// <summary>
    /// Device-buffer dimensions for a logical <paramref name="bounds"/> at density <paramref name="w"/>.
    /// The legacy custom-effect contract sizes the local buffer from the logical dimensions only;
    /// a fractional logical origin does not add a rounding pixel.
    /// </summary>
    public static (int Width, int Height) DeviceBufferSize(Rect bounds, float w)
    {
        int width = w == 1f ? (int)bounds.Width : (int)MathF.Ceiling(bounds.Width * w);
        int height = w == 1f ? (int)bounds.Height : (int)MathF.Ceiling(bounds.Height * w);
        return (width, height);
    }

    /// <summary>
    /// Gets the canonical composition-device footprint allocated for logical bounds at a concrete density.
    /// The origin is retained because fractional logical positions can add a rounding pixel to the buffer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="w"/> is non-finite or not positive.
    /// </exception>
    public static PixelRect DeviceBufferBounds(Rect bounds, float w)
    {
        if (!float.IsFinite(w) || w <= 0)
            throw new ArgumentOutOfRangeException(nameof(w), w, "Buffer density must be positive and finite.");

        return PixelRect.FromRect(bounds, w);
    }

    /// <summary>
    /// The density <see cref="CreateTarget"/> will allocate for <paramref name="bounds"/>,
    /// after applying the legacy per-buffer dimension clamp.
    /// </summary>
    public float ResolveTargetDensity(Rect bounds)
        => RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            new Rect(default, bounds.Size),
            WorkingScale);

    public EffectTarget CreateTarget(Rect bounds)
        => CreateTargetCore(bounds, WorkingScale);

    private EffectTarget CreateTargetCore(Rect bounds, float requestedDensity)
    {
        float w = requestedDensity;
        // Re-clamp at allocation site: bounds may exceed what node-level clamps saw.
        float fit = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            new Rect(default, bounds.Size),
            w);
        if (fit < w)
        {
            s_logger.LogWarning(
                "CreateTarget clamped the working scale {From} -> {To} to keep the buffer within the GPU axis limit (bounds {Bounds}). Use the returned target's Scale for output device math, not context.WorkingScale.",
                w, fit, bounds);
            w = fit;
        }

        PixelPoint deviceOrigin = DeviceBufferBounds(
            bounds.Translate(_deviceGridOffset),
            w).Position;
        (int width, int height) = DeviceBufferSize(bounds, w);
        var deviceBounds = new PixelRect(
            deviceOrigin,
            new PixelSize(width, height));
        return AllocateTarget(bounds, w, deviceBounds);
    }

    /// <summary>
    /// Creates a replacement target with the source's complete physical footprint and current
    /// logical placement. Use this for same-bounds raster effects so fractional-origin pixels and
    /// raster aprons are preserved.
    /// </summary>
    public EffectTarget CreateTargetLike(EffectTarget source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            return new EffectTarget();

        using var renderTarget = RenderTarget.Create(source.DeviceBounds.Width, source.DeviceBounds.Height);
        if (renderTarget != null)
        {
            return source.CreateReplacement(renderTarget);
        }

        s_logger.LogWarning(
            "Custom-effect target allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); returning an empty target.",
            source.DeviceBounds.Width,
            source.DeviceBounds.Height,
            source.Scale.Value,
            source.Bounds);
        return new EffectTarget();
    }

    /// <summary>
    /// Wraps a caller-created target as a replacement with the source's logical placement,
    /// density, physical footprint, device-grid alignment, and legacy placement mode.
    /// </summary>
    /// <remarks>
    /// The returned effect target owns a shallow copy; the caller retains ownership of
    /// <paramref name="renderTarget"/>.
    /// </remarks>
    public EffectTarget CreateReplacement(
        EffectTarget source,
        RenderTarget renderTarget)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(renderTarget);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
        {
            throw new ArgumentException(
                "The source must have a materialized target and concrete scale.",
                nameof(source));
        }
        if (renderTarget.Width != source.DeviceBounds.Width
            || renderTarget.Height != source.DeviceBounds.Height)
        {
            throw new ArgumentException(
                $"The replacement render target must match the source device footprint "
                + $"{source.DeviceBounds.Width}x{source.DeviceBounds.Height}.",
                nameof(renderTarget));
        }

        return source.CreateReplacement(renderTarget);
    }

    /// <summary>
    /// Creates a child shader that maps destination backing-buffer coordinates to the source
    /// target's current physical raster placement.
    /// </summary>
    /// <remarks>The caller owns and must dispose the returned shader.</remarks>
    public SKShader CreateMappedInputShader(
        EffectTarget source,
        EffectTarget destination,
        SKShader sourceShader)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sourceShader);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            throw new ArgumentException("The source must have a materialized target and concrete scale.", nameof(source));
        if (destination.RenderTarget is null || destination.Scale.IsUnbounded)
        {
            throw new ArgumentException(
                "The destination must have a materialized target and concrete scale.",
                nameof(destination));
        }

        return sourceShader.WithLocalMatrix(
            RasterShaderMapping.CreateLocalMatrix(
                destination.Scale.Value,
                source.Scale.Value,
                destination.RasterBounds,
                source.RasterBounds));
    }

    /// <summary>
    /// Supplies a borrowed GPU-backed snapshot shader for a materialized source, mapped into the
    /// destination's backing-buffer coordinates.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="use"/> ran. <see langword="false"/> when the source
    /// could not be read back under <see cref="RenderIntent.Preview"/>: the callback never ran, so the
    /// caller must keep its source target instead of committing a destination it never painted.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The source could not be read back under <see cref="RenderIntent.Delivery"/>.
    /// </exception>
    /// <remarks>
    /// The shader and its backing image are valid only during <paramref name="use"/>. The callback must
    /// complete every draw that references the shader and must not retain or dispose it.
    /// </remarks>
    public bool UseMappedInputShader<TState>(
        EffectTarget source,
        EffectTarget destination,
        TState state,
        Action<TState, SKShader> use,
        SKShaderTileMode x = SKShaderTileMode.Decal,
        SKShaderTileMode y = SKShaderTileMode.Decal)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(use);
        if (!Enum.IsDefined(x))
            throw new ArgumentOutOfRangeException(nameof(x), x, "The shader tile mode is invalid.");
        if (!Enum.IsDefined(y))
            throw new ArgumentOutOfRangeException(nameof(y), y, "The shader tile mode is invalid.");
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            throw new ArgumentException("The source must have a materialized target and concrete scale.", nameof(source));
        if (source.RenderTarget.Value is null)
            throw new ArgumentException("The source target has no backing surface to sample.", nameof(source));
        if (destination.RenderTarget is null || destination.Scale.IsUnbounded)
        {
            throw new ArgumentException(
                "The destination must have a materialized target and concrete scale.",
                nameof(destination));
        }

        source.RenderTarget.PrepareForSampling();
        using SKImage? image = source.RenderTarget.Value.Snapshot();
        if (image is null)
        {
            ThrowIfDeliveryReadbackFailure(Intent, source.DeviceBounds);
            s_logger.LogWarning(
                "The source surface could not be snapshotted for sampling ({Width}x{Height} px); the preview keeps the source pixels.",
                source.DeviceBounds.Width,
                source.DeviceBounds.Height);
            return false;
        }

        using SKShader sourceShader = image.ToShader(x, y);
        using SKShader mappedShader = CreateMappedInputShader(source, destination, sourceShader);
        use(state, mappedShader);
        return true;
    }

    // The intent alone decides degrade-vs-fail, independently of the working-scale ceiling:
    // a delivery render must not ship a frame the effect was never applied to.
    internal static void ThrowIfDeliveryReadbackFailure(RenderIntent intent, PixelRect footprint)
    {
        if (intent == RenderIntent.Delivery)
        {
            throw new InvalidOperationException(
                $"The source surface could not be snapshotted for sampling ({footprint.Width}x{footprint.Height} px); "
                + "the delivery render fails instead of shipping an unfiltered frame.");
        }
    }

    private static EffectTarget AllocateTarget(
        Rect bounds,
        float density,
        PixelRect deviceBounds)
    {
        using var renderTarget = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        if (renderTarget != null)
        {
            Vector legacyGridOffset = deviceBounds
                .ToRect(density)
                .Position - bounds.Position;
            return new EffectTarget(
                renderTarget,
                bounds,
                EffectiveScale.At(density),
                deviceBounds,
                legacyGridOffset,
                preserveLegacyRasterPlacement: true);
        }
        else
        {
            // The empty target makes the subsequent Open() throw — log the cause before that happens.
            s_logger.LogWarning(
                "Custom-effect target allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); returning an empty target.",
                deviceBounds.Width, deviceBounds.Height, density, bounds);
            return new EffectTarget();
        }
    }

    /// <summary>
    /// Opens an <see cref="ImmediateCanvas"/> over <paramref name="target"/>'s buffer.
    /// Throws if the target is empty (allocation failed in <see cref="CreateTarget"/>).
    /// </summary>
    public ImmediateCanvas Open(EffectTarget target)
    {
        if (target.RenderTarget == null)
        {
            throw new InvalidOperationException(
                "Cannot Open an empty EffectTarget — its buffer allocation failed (see the preceding " +
                "CreateTarget warning for the size/cause). The effect fails visibly rather than rendering partially.");
        }

        // Prefer the target's concrete Scale (may be clamped below WorkingScale by CreateTarget).
        float density = target.Scale.IsUnbounded ? WorkingScale : target.Scale.Value;
        return new ImmediateCanvas(
            target.RenderTarget,
            density,
            MaxWorkingScale,
            logicalSize: target.Bounds.Size,
            intent: Intent);
    }
}
