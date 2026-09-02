using Beutl.Graphics.Backend;
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
    private readonly DrawableBrushMaterializer? _drawableBrushMaterializer;
    private readonly bool _useExecutorManagedCanvas;
    private readonly RenderTargetLeaseSession? _renderTargetLeaseSession;
    private readonly int? _maxBufferDimension;

    internal CustomFilterEffectContext(
        EffectTargets targets,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        Vector? deviceGridOffset = null,
        DrawableBrushMaterializer? drawableBrushMaterializer = null,
        bool useExecutorManagedCanvas = false,
        RenderTargetLeaseSession? renderTargetLeaseSession = null,
        int? maxBufferDimension = null,
        Rect? targetDomain = null)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");
        if (maxBufferDimension is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBufferDimension),
                maxBufferDimension,
                "The maximum buffer dimension must be positive.");
        }

        _maxBufferDimension = maxBufferDimension;
        Targets = targets;
        _deviceGridOffset = deviceGridOffset
            ?? (targets.Count > 0 ? targets[0].DeviceGridOffset : default);
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        TargetDomain = targetDomain;
        Intent = intent;
        Purpose = purpose;
        _drawableBrushMaterializer = drawableBrushMaterializer;
        _useExecutorManagedCanvas = useExecutorManagedCanvas;
        _renderTargetLeaseSession = renderTargetLeaseSession;
    }

    public EffectTargets Targets { get; }

    /// <summary>The render request's output scale <c>s_out</c>, not a ceiling on this effect's working scale.</summary>
    public float OutputScale { get; }

    /// <summary>
    /// Gets the nominal working density. Use <see cref="ResolveTargetDensity"/> or the target scale after clamping.
    /// </summary>
    public float WorkingScale { get; }

    /// <summary>Working-scale ceiling forwarded into canvases from <see cref="Open"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>Gets the request-level delivery region, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The value is not mapped into effect-local space. Use <see cref="Rect.TransformToDeliveredAABB"/> when
    /// bounding a target transform.
    /// </remarks>
    public Rect? TargetDomain { get; }

    /// <summary>Gets the maximum allocation extent on either axis.</summary>
    /// <remarks>
    /// Resolved per call because the graphics context may become available after this context is created.
    /// </remarks>
    public int MaxBufferDimension
        => _maxBufferDimension ?? RenderScaleUtilities.ResolveMaxBufferDimension();

    /// <summary>Gets the effect-local to composition-device grid translation.</summary>
    public Vector DeviceGridOffset => _deviceGridOffset;

    /// <summary>Gets the explicit preview or delivery classification for this execution.</summary>
    public RenderIntent Intent { get; }

    /// <summary>Gets the explicit request purpose for this execution.</summary>
    public RenderRequestPurpose Purpose { get; }

    internal DrawableBrushMaterializer? DrawableBrushMaterializer => _drawableBrushMaterializer;

    internal bool UsesExecutorManagedCanvas => _useExecutorManagedCanvas;

    internal RenderTargetLeaseSession? RenderTargetLeaseSession => _renderTargetLeaseSession;

    internal BrushConstructor CreateBrushConstructor(
        Rect bounds,
        Brush.Resource? brush,
        BlendMode blendMode,
        float scale)
        => new(
            bounds,
            brush,
            blendMode,
            scale,
            MaxWorkingScale,
            Intent,
            _drawableBrushMaterializer,
            _renderTargetLeaseSession);

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
    /// The effect-item custom-effect contract sizes the local buffer from the logical dimensions only;
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
    /// after applying the effect-item per-buffer dimension clamp.
    /// </summary>
    public float ResolveTargetDensity(Rect bounds)
        => RenderScaleUtilities.ClampWorkingScaleToDeviceBufferBudget(
            new Rect(default, bounds.Size),
            WorkingScale,
            MaxBufferDimension);

    /// <summary>
    /// Creates a target for the requested logical bounds at the resolved working density.
    /// </summary>
    /// <remarks>
    /// If allocation fails, <see cref="RenderIntent.Preview"/> logs the failure and returns an empty
    /// target, while <see cref="RenderIntent.Delivery"/> throws.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The allocation failed during a <see cref="RenderIntent.Delivery"/> render.
    /// </exception>
    public EffectTarget CreateTarget(Rect bounds)
        => CreateTargetCore(bounds, WorkingScale);

    private EffectTarget CreateTargetCore(Rect bounds, float requestedDensity)
    {
        float w = requestedDensity;
        // Re-clamp at allocation site: bounds may exceed what node-level clamps saw, and planning's budget
        // is the engine ceiling rather than what this device can attach.
        float fit = RenderScaleUtilities.ClampWorkingScaleToDeviceBufferBudget(
            new Rect(default, bounds.Size),
            w,
            MaxBufferDimension);
        if (fit < w)
        {
            s_logger.LogWarning(
                "CreateTarget clamped the working scale {From} -> {To} to keep the buffer within the {Limit} px GPU axis limit (bounds {Bounds}). Use the returned target's Scale for output device math, not context.WorkingScale.",
                w, fit, MaxBufferDimension, bounds);
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
    /// <remarks>
    /// An unmaterialized or unbounded <paramref name="source"/> is a legitimate skip and returns an
    /// empty target for either intent. If the replacement allocation itself fails,
    /// <see cref="RenderIntent.Preview"/> logs the failure and returns an empty target so the caller
    /// can keep the source, while <see cref="RenderIntent.Delivery"/> throws.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The replacement allocation failed during a <see cref="RenderIntent.Delivery"/> render.
    /// </exception>
    public EffectTarget CreateTargetLike(EffectTarget source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            return new EffectTarget();

        EffectTarget? replacement = AllocateReplacement(source, FactoryBackedSession);
        if (replacement != null)
        {
            return replacement;
        }

        if (Intent == RenderIntent.Delivery)
        {
            throw new InvalidOperationException(
                $"Custom-effect replacement target allocation failed ({source.DeviceBounds.Width}x{source.DeviceBounds.Height} px, "
                + $"target density {source.Scale.Value}, bounds {source.Bounds}); "
                + "the delivery render fails instead of shipping an unprocessed frame.");
        }

        s_logger.LogWarning(
            "Custom-effect replacement target allocation failed ({Width}x{Height} px, target density {TargetDensity}, bounds {Bounds}); returning an empty target so the preview can keep the source pixels.",
            source.DeviceBounds.Width,
            source.DeviceBounds.Height,
            source.Scale.Value,
            source.Bounds);
        _renderTargetLeaseSession?.MarkContentDropped();
        return new EffectTarget();
    }

    /// <remarks>
    /// A declined allocation leaves the caller holding the unfiltered source, and the request is told it
    /// dropped content. Without that, an executor can publish the unfiltered frame into a persistent
    /// render-node cache or a backdrop snapshot, and a later hit keeps bypassing the effect long after the
    /// factory recovered. This reaches only a preview: a lease session declines by throwing under
    /// <see cref="RenderIntent.Delivery"/>, so a delivery render fails before it gets here.
    /// </remarks>
    internal EffectTarget CreateNativeTargetLike(EffectTarget source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_renderTargetLeaseSession is null)
            return CreateTargetLike(source);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            return new EffectTarget();

        // Every consumer of this target is a full-frame shader pass: its load op either clears the
        // attachment or the shader provably writes every pixel, so the pool's own clear - and the two
        // layout transitions around it - would be undone before anything read them.
        EffectTarget? replacement = AllocateReplacement(
            source,
            _renderTargetLeaseSession,
            clearContents: false);
        if (replacement != null)
            return replacement;

        s_logger.LogWarning(
            "Native custom-effect replacement target allocation failed ({Width}x{Height} px, target density {TargetDensity}, bounds {Bounds}); returning an empty target so the preview can keep the source pixels.",
            source.DeviceBounds.Width,
            source.DeviceBounds.Height,
            source.Scale.Value,
            source.Bounds);
        _renderTargetLeaseSession.MarkContentDropped();
        return new EffectTarget();
    }

    /// <summary>
    /// Allocates a same-footprint replacement for <paramref name="source"/>, through the caller's lease session
    /// when there is one, and reports a declined allocation as <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A configured <see cref="IRenderTargetFactory"/> is reachable only through the session, and its targets may
    /// come from a context the global allocator knows nothing about. Going around it here would both ignore the
    /// caller's allocation policy and let a custom effect sample a factory-backed input into a foreign surface.
    /// </remarks>
    private EffectTarget? AllocateReplacement(
        EffectTarget source,
        RenderTargetLeaseSession? leaseSession,
        bool clearContents = true)
    {
        if (leaseSession is not null)
        {
            RenderTargetLease? lease = leaseSession.TryAcquire(source.DeviceBounds.Size, clearContents);
            if (lease is null)
                return null;

            try
            {
                return source.CreateReplacement(lease);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        using RenderTarget? renderTarget = RenderTarget.Create(
            source.DeviceBounds.Width,
            source.DeviceBounds.Height);
        return renderTarget is null ? null : source.CreateReplacement(renderTarget);
    }

    internal NativeFilterTextureLease AcquireNativeScratchTexture(
        IGraphicsContext graphicsContext,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(graphicsContext);
        NativeFilterTextureLease lease;
        if (_renderTargetLeaseSession is null)
        {
            lease = NativeFilterTextureLease.Own(
                graphicsContext.CreateTexture2D(width, height, TextureFormat.RGBA16Float));
        }
        else
        {
            var size = new PixelSize(width, height);
            RenderTargetLease? renderTargetLease = _renderTargetLeaseSession.TryAcquire(size);
            if (renderTargetLease is null)
                throw RenderTargetPool.CreateAllocationFailure(size);

            ITexture2D? texture = renderTargetLease.Target.Texture;
            if (texture is null
                || texture.Width != width
                || texture.Height != height
                || texture.Format != TextureFormat.RGBA16Float)
            {
                renderTargetLease.Dispose();
                throw new InvalidOperationException(
                    "A native filter scratch lease requires an exact-size RGBA16F GPU texture.");
            }

            lease = NativeFilterTextureLease.Lease(texture, renderTargetLease);
        }

        try
        {
            if (lease.Texture is not ITransparentClearableTexture clearableTexture)
            {
                throw new InvalidOperationException(
                    "A native filter scratch texture must support an ordered transparent clear.");
            }

            clearableTexture.ClearToTransparent();
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wraps a caller-created target as a replacement with the source's logical placement,
    /// density, physical footprint, device-grid alignment, and effect-item placement mode.
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
        if (source.RenderTarget.RawValue is null)
            throw new ArgumentException("The source target has no backing surface to sample.", nameof(source));
        if (destination.RenderTarget is null || destination.Scale.IsUnbounded)
        {
            throw new ArgumentException(
                "The destination must have a materialized target and concrete scale.",
                nameof(destination));
        }

        source.RenderTarget.PrepareForSampling(
            RenderTargetSamplingIntent.SameContextTextureSampling(destination.RenderTarget.RawValue.Context));
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

    private EffectTarget AllocateTarget(
        Rect bounds,
        float density,
        PixelRect deviceBounds)
    {
        Vector effectItemGridOffset = deviceBounds
            .ToRect(density)
            .Position - bounds.Position;
        EffectTarget? allocated = Allocate(bounds, density, deviceBounds, effectItemGridOffset);
        if (allocated != null)
        {
            return allocated;
        }
        else
        {
            s_logger.LogWarning(
                "Custom-effect target allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); preview returns an empty target, delivery render fails fast.",
                deviceBounds.Width, deviceBounds.Height, density, bounds);

            if (Intent == RenderIntent.Delivery)
            {
                throw new InvalidOperationException(
                    $"Custom-effect target allocation failed ({deviceBounds.Width}x{deviceBounds.Height} px, "
                    + $"w {density}, bounds {bounds}); the delivery render fails instead of shipping an incomplete frame.");
            }

            _renderTargetLeaseSession?.MarkContentDropped();
            return new EffectTarget();
        }
    }

    // Only a caller-supplied factory redirects allocation through the lease session.
    private RenderTargetLeaseSession? FactoryBackedSession
        => _renderTargetLeaseSession is { HasTargetFactory: true } session ? session : null;

    private EffectTarget? Allocate(
        Rect bounds,
        float density,
        PixelRect deviceBounds,
        Vector deviceGridOffset)
    {
        if (FactoryBackedSession is { } leaseSession)
        {
            RenderTargetLease? lease = leaseSession.TryAcquire(deviceBounds.Size);
            if (lease is null)
                return null;

            try
            {
                return EffectTarget.FromLease(
                    lease,
                    bounds,
                    EffectiveScale.At(density),
                    deviceBounds,
                    deviceGridOffset,
                    preserveImperativeRasterPlacement: true);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        using RenderTarget? renderTarget = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        return renderTarget is null
            ? null
            : new EffectTarget(
                renderTarget,
                bounds,
                EffectiveScale.At(density),
                deviceBounds,
                deviceGridOffset,
                preserveImperativeRasterPlacement: true);
    }

    /// <summary>
    /// Opens an <see cref="ImmediateCanvas"/> over <paramref name="target"/>'s buffer.
    /// Throws if the target is empty. <see cref="CreateTarget"/> can return an empty target after a
    /// Preview allocation failure; Delivery allocation failures are thrown by <see cref="CreateTarget"/>.
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
        ImmediateCanvas canvas;
        if (_useExecutorManagedCanvas)
        {
            canvas = ImmediateCanvas.CreateExecutorManaged(
                target.RenderTarget,
                density,
                MaxWorkingScale,
                target.Bounds.Size,
                Intent);
            canvas.ConfigureCustomEffectExecution();
        }
        else
        {
            canvas = new ImmediateCanvas(
                target.RenderTarget,
                Intent,
                density,
                MaxWorkingScale,
                logicalSize: target.Bounds.Size);
        }

        canvas.DrawableBrushMaterializer = _drawableBrushMaterializer;
        return canvas;
    }

    /// <summary>
    /// Creates a <see cref="FilterEffectActivator"/> that belongs to the render this callback is running
    /// inside, so a nested filter pipeline allocates its intermediates from the same place
    /// <see cref="Targets"/> came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FilterEffectActivator"/>'s public constructor builds a <i>standalone</i> activator, which
    /// allocates from the process-wide shared graphics context because it has no render to belong to. That is
    /// the wrong answer inside a custom effect: when the caller configured a
    /// <see cref="RenderNodeRendererOptions.TargetFactory"/>, <see cref="Targets"/> holds surfaces the shared
    /// allocator knows nothing about, and a standalone activator's flush buffer would meet them inside a
    /// single draw — two graphics contexts in one flush. Mint the activator here instead of constructing one.
    /// </para>
    /// <para>
    /// The returned activator carries this context's intent, purpose, output and working scales,
    /// working-scale ceiling, device grid offset, buffer budget, target domain and drawable-brush
    /// materializer, none of which the public constructor can be handed from out-of-tree code.
    /// </para>
    /// <para>
    /// The caller owns the result and disposes it; <paramref name="builder"/> and <paramref name="targets"/>
    /// stay the caller's too. Pass <see cref="EffectTargets.Clone"/> when the callback still needs
    /// <see cref="Targets"/> after the nested pipeline runs.
    /// </para>
    /// </remarks>
    /// <param name="targets">The targets the nested pipeline reads and replaces.</param>
    /// <param name="builder">The Skia filter builder the nested pipeline accumulates into.</param>
    public FilterEffectActivator CreateActivator(EffectTargets targets, SKImageFilterBuilder builder)
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
            _deviceGridOffset,
            _drawableBrushMaterializer,
            _useExecutorManagedCanvas,
            _renderTargetLeaseSession,
            _maxBufferDimension,
            TargetDomain);
    }
}
