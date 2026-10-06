using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas : IDisposable, IPopable
{
    private static readonly AsyncLocal<FlushObserverScope?> s_flushObserver = new();
    private static readonly AsyncLocal<DrawableBrushMaterializerScope?> s_drawableBrushMaterializer = new();
    private static readonly AsyncLocal<RenderTargetLeaseSessionScope?> s_renderTargetLeaseSession = new();
    private static readonly Lazy<SKRuntimeEffect> s_rectCoverageEffect = new(CreateRectCoverageEffect);
    private static readonly Lazy<SKRuntimeEffect> s_opacityScaleEffect = new(CreateOpacityScaleEffect);
    private static readonly SKSamplingOptions s_bitmapSampling = new(SKCubicResampler.Mitchell);

    // A tent kernel has no negative lobe, so a resampled composite cannot emit a value outside the
    // range of the samples it interpolated.
    private static readonly SKSamplingOptions s_compositeSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly RenderTarget _renderTargetValue;
    private readonly Dispatcher? _dispatcher;
    private readonly SKPaint _sharedFillPaint = new();
    private readonly SKPaint _sharedStrokePaint = new();
    private readonly Stack<CanvasPushedState> _states = new();
    internal bool HasActiveSaveLayer => _states.Any(static state => state is
        CanvasPushedState.LayerPushedState
        or CanvasPushedState.MaskPushedState
        or CanvasPushedState.BlendModePushedState
        or CanvasPushedState.OpacityPushedState);
    private int _disposeClaimed;
    private Matrix _currentTransform;
    // Base CTM = CreateScale(SurfaceDensity); identity when density == 1.
    private readonly Matrix _baseTransform;
    // SKCanvas save depth pinning the base CTM. -1 when density == 1 (no base Save).
    // Default -1 so Dispose is safe if the constructor throws before the base Save.
    private readonly int _baseSaveCount = -1;
    // Density of the current coordinate space: SurfaceDensity normally, 1 inside PushDeviceSpace().
    private float _currentDensity;
    // Base matrix for the Set transform operator: _baseTransform normally, identity inside PushDeviceSpace().
    private Matrix _currentBaseTransform;
    private RenderExecutionSessionToken? _executionToken;
    private CallbackCanvasCapability? _callbackCapability;
    private bool _isReplayingTargetScope;
    private BlendMode? _directBlendMode;
    private bool _productRectangleCoverage;
    private int _callbackStateFloor;
    private readonly bool _flushOnDispose;
    private bool _allowDeferredSameContextSampling;
    private bool _submitOnDispose;

    /// <param name="intent">
    /// The classification of the render this canvas paints for. It precedes the optional parameters because a
    /// trailing default would let a delivery host silently inherit <see cref="RenderIntent.Preview"/>, whose
    /// policy is to drop an unallocatable brush intermediate: the export would ship a frame with a hole in it
    /// and report success.
    /// </param>
    /// <param name="drawableBrushMaterializer">
    /// The hook that rasterizes a <see cref="DrawableBrush.Resource"/>'s nested content, or
    /// <see langword="null"/> to inherit the ambient one. A canvas left without either paints a
    /// <see cref="DrawableBrush"/> transparent under <see cref="RenderIntent.Preview"/> and throws under
    /// <see cref="RenderIntent.Delivery"/>.
    /// </param>
    public ImmediateCanvas(RenderTarget renderTarget, RenderIntent intent, float density = 1f,
        float maxWorkingScale = float.PositiveInfinity, Size logicalSize = default,
        DrawableBrushMaterializer? drawableBrushMaterializer = null)
        : this(renderTarget, density, maxWorkingScale, logicalSize, intent, flushOnDispose: true,
            deviceOrigin: default, drawableBrushMaterializer)
    {
    }

    private ImmediateCanvas(
        RenderTarget renderTarget,
        float density,
        float maxWorkingScale,
        Size logicalSize,
        RenderIntent intent,
        bool flushOnDispose,
        PixelPoint deviceOrigin,
        DrawableBrushMaterializer? drawableBrushMaterializer = null)
    {
        ArgumentNullException.ThrowIfNull(renderTarget);
        if (density <= 0f || !float.IsFinite(density))
            throw new ArgumentOutOfRangeException(nameof(density), density,
                "Density must be a positive finite value.");
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown render intent.");

        _dispatcher = Dispatcher.Current;
        _flushOnDispose = flushOnDispose;
        _renderTargetValue = renderTarget;
        Canvas = _renderTarget.RawValue.Canvas;
        DeviceSize = new PixelSize(renderTarget.Width, renderTarget.Height);
        DeviceOrigin = deviceOrigin;
        LogicalSize = logicalSize.IsDefault ? DeviceSize.ToSize(density) : logicalSize;
        SurfaceDensity = density;
        _currentDensity = density;
        MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        Intent = intent;
        DrawableBrushMaterializer = drawableBrushMaterializer ?? s_drawableBrushMaterializer.Value?.Materializer;
        RenderTargetLeaseSession = s_renderTargetLeaseSession.Value?.LeaseSession;
        if (density == 1f)
        {
            _baseTransform = Matrix.Identity;
            _baseSaveCount = -1;
            _currentTransform = Canvas.TotalMatrix.ToMatrix();
        }
        else
        {
            // Pin the base scale below all Push/Pop so RestoreToCount cannot unwind past it.
            _baseTransform = Matrix.CreateScale(density, density);
            _baseSaveCount = Canvas.Save();
            Canvas.SetMatrix((SKMatrix44)_baseTransform.ToSKMatrix());
            _currentTransform = _baseTransform;
        }

        _currentBaseTransform = _baseTransform;
        _renderTarget.BeginDraw();
    }

    private ImmediateCanvas(ImmediateCanvas parent)
    {
        parent.VerifyAccess();
        _dispatcher = Dispatcher.Current;
        _flushOnDispose = false;
        _renderTargetValue = parent._renderTargetValue;
        Canvas = parent.Canvas;
        DeviceSize = parent.DeviceSize;
        DeviceOrigin = parent.DeviceOrigin;
        LogicalSize = parent.LogicalSize;
        SurfaceDensity = parent.SurfaceDensity;
        _currentDensity = parent._currentDensity;
        _directBlendMode = parent._directBlendMode;
        _productRectangleCoverage = parent._productRectangleCoverage;
        _allowDeferredSameContextSampling = parent._allowDeferredSameContextSampling;
        MaxWorkingScale = parent.MaxWorkingScale;
        Intent = parent.Intent;
        DrawableBrushMaterializer = parent.DrawableBrushMaterializer;
        RenderTargetLeaseSession = parent.RenderTargetLeaseSession;
        _baseTransform = parent._currentBaseTransform;
        _currentBaseTransform = parent._currentBaseTransform;
        _baseSaveCount = Canvas.Save();
        _currentTransform = Canvas.TotalMatrix.ToMatrix();
        _renderTargetValue.BeginDraw();
    }

    ~ImmediateCanvas()
    {
        // A finalizer must never throw — an unhandled exception on the finalizer thread aborts the process.
        // Dispose can throw on a leaked / half-built canvas (dispatcher Invoke or context-lost GPU op), so the
        // GC path swallows; explicit Dispose() still surfaces errors.
        try
        {
            Dispose(disposing: false);
        }
        catch
        {
            // ignore — never crash the finalizer thread
        }
    }

    public bool IsDisposed { get; private set; }

    public BlendMode BlendMode { get; set; } = BlendMode.SrcOver;

    public float Opacity { get; set; } = 1;

    /// <summary>The logical viewport, independent of the device pixel size.</summary>
    public Size LogicalSize { get; }

    /// <summary>The physical backing-surface size in device pixels (<c>ceil(LogicalSize × SurfaceDensity)</c>).</summary>
    public PixelSize DeviceSize { get; }

    internal PixelPoint DeviceOrigin { get; }

    /// <summary>
    /// Pixel density of the current coordinate space. Equals <see cref="SurfaceDensity"/> normally;
    /// 1 inside a <see cref="PushDeviceSpace"/> block.
    /// </summary>
    public float Density => _currentDensity;

    /// <summary>
    /// The immutable density the backing surface is rasterized at (device px per logical unit), fixed at
    /// construction. On the root canvas this is <c>s_out</c>; on a nested buffer it is <c>w</c>.
    /// </summary>
    public float SurfaceDensity { get; }

    /// <summary>Working-scale ceiling forwarded into nested pulls. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// Preview or delivery classification, inherited by brush intermediates and nested requests opened from
    /// this canvas. <see cref="RenderIntent.Preview"/> degrades on an allocation failure;
    /// <see cref="RenderIntent.Delivery"/> fails instead of dropping the contribution.
    /// </summary>
    public RenderIntent Intent { get; }

    /// <summary>
    /// Runtime hook that materializes a <see cref="DrawableBrush.Resource"/>'s nested content into an
    /// <see cref="SKImage"/> covering <paramref name="bounds"/> at <paramref name="scale"/> device px per
    /// logical unit. The executor sets it while a canvas is open and clears it when the canvas closes;
    /// a null hook leaves DrawableBrush materialization unavailable, which degrades the fill to transparent
    /// under <see cref="RenderIntent.Preview"/> and fails the render under <see cref="RenderIntent.Delivery"/>.
    /// </summary>
    public DrawableBrushMaterializer? DrawableBrushMaterializer { get; internal set; }

    /// <summary>
    /// The render pass's target lease session, or <see langword="null"/> outside one. Brush-owned intermediates
    /// allocate through it so a caller-supplied <see cref="IRenderTargetFactory"/> is honoured.
    /// </summary>
    internal RenderTargetLeaseSession? RenderTargetLeaseSession { get; set; }

    /// <summary>
    /// Creates a brush constructor bound to this canvas's current density, working-scale ceiling and
    /// render intent, so a caller painting onto this canvas never has to restate them.
    /// </summary>
    /// <param name="bounds">The logical frame the brush maps onto.</param>
    /// <param name="brush">The brush to paint with, or <see langword="null"/> for no paint.</param>
    /// <param name="blendMode">The blend mode to configure.</param>
    public BrushConstructor CreateBrushConstructor(Rect bounds, Brush.Resource? brush, BlendMode blendMode)
        => new(bounds, brush, blendMode, _currentDensity, MaxWorkingScale, Intent, DrawableBrushMaterializer, RenderTargetLeaseSession);

    public Matrix Transform
    {
        get { return _currentTransform; }
        // Internal: bypasses the base CTM. Public mutation goes through PushTransform.
        internal set
        {
            if (_currentTransform == value)
                return;

            _currentTransform = value;
            Canvas.SetMatrix((SKMatrix44)_currentTransform.ToSKMatrix());
        }
    }

    internal SKCanvas Canvas { get; }

    internal static ImmediateCanvas CreateExecutorManaged(
        RenderTarget renderTarget,
        float density,
        float maxWorkingScale,
        Size logicalSize,
        RenderIntent intent,
        PixelPoint deviceOrigin = default)
        => new(
            renderTarget,
            density,
            maxWorkingScale,
            logicalSize,
            intent,
            flushOnDispose: false,
            deviceOrigin);

    internal void ConfigureCustomEffectExecution()
    {
        VerifyAccess();
        if (_flushOnDispose)
        {
            throw new InvalidOperationException(
                "Custom-effect execution requires an executor-managed canvas.");
        }

        _allowDeferredSameContextSampling = true;
        _submitOnDispose = true;
    }

    internal RenderTarget _renderTarget
    {
        get
        {
            if (_callbackCapability is not null && !_isReplayingTargetScope)
            {
                throw new InvalidOperationException(
                    "The backing render target cannot be extracted from a guarded callback canvas.");
            }

            return _renderTargetValue;
        }
    }

    public void Clear()
    {
        VerifyPixelOperation(isClear: true);
        Canvas.Clear();
    }

    public void Clear(Color color)
    {
        VerifyPixelOperation(isClear: true);
        Canvas.Clear(color.ToSKColor());
    }

    internal void ReplaceAffectedRegion(Color color)
    {
        VerifyPixelOperation();
        using var paint = new SKPaint
        {
            Color = color.ToSKColor(),
            BlendMode = SKBlendMode.Src,
            IsAntialias = false,
        };
        Canvas.DrawPaint(paint);
    }

    public void ClipRect(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        Canvas.ClipRect(clip.ToSKRect(), operation.ToSKClipOperation());
    }

    /// <summary>
    /// Intersects the clip with every device pixel <paramref name="clip"/> touches.
    /// </summary>
    /// <remarks>
    /// A non-antialiased Skia clip snaps to the nearest device pixel, so a rect that falls between
    /// pixel centres cuts into the content it is meant to bound: an edge column loses its partial
    /// coverage, and a footprint narrower than a pixel rounds away entirely. Widening the rect to whole
    /// pixels first keeps a conservative bound conservative, which is the only direction it may err in.
    /// Antialiasing the clip instead would be wrong, because the bound's own edge coverage would then
    /// multiply the content's.
    /// </remarks>
    internal void ClipRectCoveringDevicePixels(Rect clip)
    {
        VerifyAccess();
        SKMatrix transform = Canvas.TotalMatrix;
        if (!transform.TryInvert(out SKMatrix inverse))
        {
            ClipRect(clip);
            return;
        }

        if (transform.SkewX == 0
            && transform.SkewY == 0
            && transform.Persp0 == 0
            && transform.Persp1 == 0)
        {
            // An axis-aligned map takes a rect to a rect, so the covering pixels have an exact
            // preimage. A bound already sitting on the device grid therefore stays untouched.
            SKRect device = transform.MapRect(clip.ToSKRect());
            var covering = new SKRect(
                MathF.Floor(device.Left),
                MathF.Floor(device.Top),
                MathF.Ceiling(device.Right),
                MathF.Ceiling(device.Bottom));
            if (IsFinite(covering))
            {
                SKRect local = inverse.MapRect(covering);
                if (IsFinite(local))
                {
                    ClipRect(new Rect(local.Left, local.Top, local.Width, local.Height));
                    return;
                }
            }

            ClipRect(clip);
            return;
        }

        // Rotation and skew take the rect to a parallelogram, which has no pixel-aligned preimage.
        // Widening by whatever one device pixel measures along each local axis still covers the snap.
        float horizontal = MathF.Abs(inverse.ScaleX) + MathF.Abs(inverse.SkewX);
        float vertical = MathF.Abs(inverse.SkewY) + MathF.Abs(inverse.ScaleY);
        ClipRect(
            float.IsFinite(horizontal) && float.IsFinite(vertical)
                ? clip.Inflate(new Thickness(horizontal, vertical))
                : clip);

        static bool IsFinite(SKRect rect)
            => float.IsFinite(rect.Left)
               && float.IsFinite(rect.Top)
               && float.IsFinite(rect.Right)
               && float.IsFinite(rect.Bottom);
    }

    public void ClipPath(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        VerifyCallbackResource(geometry, nameof(geometry));
        Canvas.ClipPath(geometry.GetCachedPath(), operation.ToSKClipOperation(), true);
    }

    public void Dispose() => Dispose(disposing: true);

    private void Dispose(bool disposing)
    {
        if (_executionToken is not null && !IsDisposed)
        {
            throw new InvalidOperationException(
                "Executor-managed callback canvases cannot be disposed by callback code.");
        }

        // Claimed here, not inside CloseCore: Run can return with the cleanup still queued, so a
        // second Dispose would see IsDisposed false and queue a rival one, double-disposing the paints.
        if (Interlocked.Exchange(ref _disposeClaimed, 1) != 0)
        {
            return;
        }

        if (!disposing)
        {
            GpuResourceRelease.DispatchFinalizer(
                _dispatcher,
                () => CloseCore(_flushOnDispose, _submitOnDispose));
            return;
        }

        GpuResourceRelease.Run(
            _dispatcher,
            () => CloseCore(_flushOnDispose, _submitOnDispose));
    }

    private void CloseCore(bool flush, bool submit)
    {
        // Must suppress the finalizer before any backend operation that might throw.
        IsDisposed = true;
        GC.SuppressFinalize(this);
        try
        {
            if (flush && GraphicsContextFactory.SharedContext is { } context)
            {
                context.SkiaContext.Flush(true, true);
                RecordFlush(ImmediateCanvasFlushKind.CanvasClose);
                GpuResourceReclaimQueue.DrainAfterContextSync();
            }
            else if (submit && _renderTarget.RawValue.Context is GRContext submitContext)
            {
                submitContext.Flush(true, false);
                RecordFlush(ImmediateCanvasFlushKind.CanvasSubmit);
            }

            while (_states.TryPop(out CanvasPushedState? state))
            {
                state.Pop(this);
            }

            // Undo the base Save() (density != 1). Guard Canvas.Handle: SkiaSharp may have
            // zeroed it during GrContext teardown; RestoreToCount on a zero Handle SIGSEGVs.
            if (_baseSaveCount >= 0 && Canvas is not null && Canvas.Handle != IntPtr.Zero)
            {
                Canvas.RestoreToCount(_baseSaveCount);
            }
        }
        catch
        {
            // Best-effort backend-state cleanup; disposal must still release managed paints.
        }
        finally
        {
            DrawableBrushMaterializer = null;
            RenderTargetLeaseSession = null;
            _sharedFillPaint.Dispose();
            _sharedStrokePaint.Dispose();
        }
    }

    private void ConfigureStrokePaint(Rect bounds, Pen.Resource? pen, BlendMode blendMode = BlendMode.SrcOver, float? scale = null)
    {
        _sharedStrokePaint.Reset();

        if (pen != null && pen.Thickness != 0)
        {
            _sharedStrokePaint.IsStroke = false;
            new BrushConstructor(
                bounds,
                pen.Brush,
                ResolvePaintBlendMode(blendMode),
                scale ?? _currentDensity,
                MaxWorkingScale,
                Intent,
                DrawableBrushMaterializer,
            RenderTargetLeaseSession).ConfigurePaint(_sharedStrokePaint);
        }
    }

    private void ConfigureFillPaint(Rect bounds, Brush.Resource? brush, BlendMode blendMode = BlendMode.SrcOver, float? scale = null)
    {
        _sharedFillPaint.Reset();
        new BrushConstructor(
            bounds,
            brush,
            ResolvePaintBlendMode(blendMode),
            scale ?? _currentDensity,
            MaxWorkingScale,
            Intent,
            DrawableBrushMaterializer,
            RenderTargetLeaseSession).ConfigurePaint(_sharedFillPaint);
    }

    private BlendMode ResolvePaintBlendMode(BlendMode fallback)
        => _directBlendMode ?? fallback;

    private void ApplyDirectBlendMode(SKPaint paint)
    {
        if (_directBlendMode is { } blendMode)
            paint.BlendMode = (SKBlendMode)blendMode;
    }
}
