using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics;

internal enum ImmediateCanvasFlushKind : byte
{
    CanvasClose,
    SourceSurface,
    PrepareForSampling,
}

public partial class ImmediateCanvas : IDisposable, IPopable
{
    private static readonly AsyncLocal<FlushObserverScope?> s_flushObserver = new();
    private static readonly AsyncLocal<PixelOperationObserverScope?> s_pixelOperationObserver = new();
    private static readonly Lazy<SKRuntimeEffect> s_rectCoverageEffect = new(CreateRectCoverageEffect);

    // A tent kernel has no negative lobe, so a resampled composite cannot emit a value outside the
    // range of the samples it interpolated.
    private static readonly SKSamplingOptions s_compositeSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly RenderTarget _renderTargetValue;
    private readonly Dispatcher? _dispatcher;
    private readonly SKPaint _sharedFillPaint = new();
    private readonly SKPaint _sharedStrokePaint = new();
    private readonly Stack<CanvasPushedState> _states = new();
    private readonly Stack<ClipReplayOperation> _clipReplayOperations = new();
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

    public ImmediateCanvas(RenderTarget renderTarget, float density = 1f,
        float maxWorkingScale = float.PositiveInfinity, Size logicalSize = default,
        RenderIntent intent = RenderIntent.Preview)
        : this(renderTarget, density, maxWorkingScale, logicalSize, intent, flushOnDispose: true,
            deviceOrigin: default)
    {
    }

    private ImmediateCanvas(
        RenderTarget renderTarget,
        float density,
        float maxWorkingScale,
        Size logicalSize,
        RenderIntent intent,
        bool flushOnDispose,
        PixelPoint deviceOrigin)
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
        Canvas = _renderTarget.Value.Canvas;
        DeviceSize = new PixelSize(renderTarget.Width, renderTarget.Height);
        DeviceOrigin = deviceOrigin;
        LogicalSize = logicalSize.IsDefault ? DeviceSize.ToSize(density) : logicalSize;
        SurfaceDensity = density;
        _currentDensity = density;
        MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        Intent = intent;
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
        MaxWorkingScale = parent.MaxWorkingScale;
        Intent = parent.Intent;
        DrawableBrushMaterializer = parent.DrawableBrushMaterializer;
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
    /// a null hook degrades an unlowered DrawableBrush fill to transparent.
    /// </summary>
    internal Func<DrawableBrush.Resource, Rect, float, SKImage>? DrawableBrushMaterializer { get; set; }

    /// <summary>
    /// Creates a brush constructor bound to this canvas's current density, working-scale ceiling and
    /// render intent, so a caller painting onto this canvas never has to restate them.
    /// </summary>
    /// <param name="bounds">The logical frame the brush maps onto.</param>
    /// <param name="brush">The brush to paint with, or <see langword="null"/> for no paint.</param>
    /// <param name="blendMode">The blend mode to configure.</param>
    public BrushConstructor CreateBrushConstructor(Rect bounds, Brush.Resource? brush, BlendMode blendMode)
        => new(bounds, brush, blendMode, _currentDensity, MaxWorkingScale, Intent, DrawableBrushMaterializer);

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

    internal static IDisposable ObserveFlushes(Action<ImmediateCanvasFlushKind> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new FlushObserverScope(s_flushObserver.Value, observer);
        s_flushObserver.Value = scope;
        return scope;
    }

    internal static IDisposable ObservePixelOperations(Action observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new PixelOperationObserverScope(s_pixelOperationObserver.Value, observer);
        s_pixelOperationObserver.Value = scope;
        return scope;
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
        RecordPixelOperation();
        Canvas.Clear();
    }

    public void Clear(Color color)
    {
        VerifyPixelOperation(isClear: true);
        RecordPixelOperation();
        Canvas.Clear(color.ToSKColor());
    }

    internal void ReplaceAffectedRegion(Color color)
    {
        VerifyPixelOperation();
        RecordPixelOperation();
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
            GpuResourceRelease.DispatchFinalizer(_dispatcher, () => CloseCore(_flushOnDispose));
            return;
        }

        GpuResourceRelease.Run(_dispatcher, () => CloseCore(_flushOnDispose));
    }

    public void DrawSurface(SKSurface surface, Point point)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        RecordPixelOperation();
        Canvas.DrawSurface(surface, point.X, point.Y, _sharedFillPaint);

        surface.Flush(true, true);
        RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
    }

    public void DrawRenderTarget(RenderTarget renderTarget, Point point)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        // NOTE: renderTargetを保持しておいて次回Flushされたときに開放すると効率的
        renderTarget.VerifyAccess();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        RecordPixelOperation();
        Canvas.DrawSurface(renderTarget.Value, point.X, point.Y, _sharedFillPaint);

        renderTarget.Value.Flush(true, true);
        RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
    }

    // Draw a buffer into a logical destination rect.
    public void DrawRenderTargetScaled(RenderTarget renderTarget, Rect dest)
        => DrawRenderTargetScaledCore(renderTarget, dest, flushSource: true);

    internal void DrawRenderTargetScaledWithoutFlush(RenderTarget renderTarget, Rect dest)
        => DrawRenderTargetScaledCore(renderTarget, dest, flushSource: false);

    internal bool CanDrawPixelAligned(
        Rect dest,
        float sourceDensity,
        PixelSize sourceSize)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        return TryGetPixelAlignedDeviceOrigin(
            dest,
            sourceDensity,
            sourceSize,
            _currentDensity,
            _currentTransform,
            out _);
    }

    internal static bool CanDrawPixelAligned(
        Rect dest,
        float sourceDensity,
        PixelSize sourceSize,
        float destinationDensity,
        Matrix destinationTransform)
        => TryGetPixelAlignedDeviceOrigin(
            dest,
            sourceDensity,
            sourceSize,
            destinationDensity,
            destinationTransform,
            out _);

    private static bool TryGetPixelAlignedDeviceOrigin(
        Rect dest,
        float sourceDensity,
        PixelSize sourceSize,
        float destinationDensity,
        Matrix destinationTransform,
        out PixelPoint deviceOrigin)
    {
        deviceOrigin = default;
        if (destinationDensity != sourceDensity
            || destinationTransform.M11 != sourceDensity
            || destinationTransform.M22 != sourceDensity
            || destinationTransform.M12 != 0
            || destinationTransform.M13 != 0
            || destinationTransform.M21 != 0
            || destinationTransform.M23 != 0
            || destinationTransform.M33 != 1)
        {
            return false;
        }

        PixelRect deviceBounds = PixelRect.FromRect(dest, sourceDensity);
        if (deviceBounds.Size != sourceSize
            || deviceBounds.ToRect(sourceDensity) != dest)
        {
            return false;
        }

        Point mappedOrigin = dest.Position * destinationTransform;
        int x = (int)MathF.Round(mappedOrigin.X);
        int y = (int)MathF.Round(mappedOrigin.Y);
        if (MathF.Abs(mappedOrigin.X - x) > 0.0001f
            || MathF.Abs(mappedOrigin.Y - y) > 0.0001f)
        {
            return false;
        }

        deviceOrigin = new PixelPoint(x, y);
        return true;
    }

    internal void DrawRenderTargetPixelsWithoutFlush(RenderTarget renderTarget, int x, int y)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        renderTarget.VerifyAccess();

        using SKImage image = renderTarget.Value.Snapshot();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = false;
        var source = SKRect.Create(image.Width, image.Height);
        var destination = SKRect.Create(x, y, image.Width, image.Height);
        using (PushDeviceSpace())
        {
            RecordPixelOperation();
            Canvas.DrawImage(
                image,
                source,
                destination,
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                _sharedFillPaint);
        }
    }

    /// <summary>
    /// Maps <paramref name="dest"/> through the active transform and reports the device origin when the
    /// mapping lands a <paramref name="sourceSize"/> buffer on exact device pixels, so a copy is lossless.
    /// </summary>
    private bool TryGetLosslessDeviceOrigin(Rect dest, PixelSize sourceSize, out PixelPoint deviceOrigin)
    {
        deviceOrigin = default;
        Matrix transform = _currentTransform;
        if (transform.M12 != 0
            || transform.M13 != 0
            || transform.M21 != 0
            || transform.M23 != 0
            || transform.M33 != 1)
        {
            return false;
        }

        Point mappedOrigin = dest.Position * transform;
        Point mappedFar = new Point(dest.Right, dest.Bottom) * transform;
        int x = (int)MathF.Round(mappedOrigin.X);
        int y = (int)MathF.Round(mappedOrigin.Y);
        if (MathF.Abs(mappedOrigin.X - x) > 0.0001f
            || MathF.Abs(mappedOrigin.Y - y) > 0.0001f
            || MathF.Abs(mappedFar.X - (x + sourceSize.Width)) > 0.0001f
            || MathF.Abs(mappedFar.Y - (y + sourceSize.Height)) > 0.0001f)
        {
            return false;
        }

        deviceOrigin = new PixelPoint(x, y);
        return true;
    }

    private void DrawRenderTargetScaledCore(RenderTarget renderTarget, Rect dest, bool flushSource)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        renderTarget.VerifyAccess();

        // Resampling a buffer that already lands on exact device pixels only softens and rings it.
        if (TryGetLosslessDeviceOrigin(
                dest,
                new PixelSize(renderTarget.Width, renderTarget.Height),
                out PixelPoint deviceOrigin))
        {
            DrawRenderTargetPixelsWithoutFlush(renderTarget, deviceOrigin.X, deviceOrigin.Y);
        }
        else
        {
            using SKImage image = renderTarget.Value.Snapshot();
            DrawImageScaled(image, dest);
        }

        if (flushSource)
        {
            renderTarget.Value.Flush(true, true);
            RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
        }
    }

    // Draw a pre-snapshotted image into a logical destination rect.
    public void DrawImageScaled(SKImage image, Rect dest)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(image, nameof(image));
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        var src = SKRect.Create(image.Width, image.Height);
        RecordPixelOperation();
        Canvas.DrawImage(image, src, dest.ToSKRect(), s_compositeSampling, _sharedFillPaint);
    }

    // Draw a surface into its own logical footprint (pixel size / density) at the given origin.
    public void DrawSurfaceScaled(SKSurface surface, Point origin, float scale)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        using SKImage image = surface.Snapshot();
        var src = SKRect.Create(image.Width, image.Height);
        var dest = SKRect.Create((float)origin.X, (float)origin.Y, image.Width / scale, image.Height / scale);
        RecordPixelOperation();
        Canvas.DrawImage(image, src, dest, s_compositeSampling, _sharedFillPaint);

        surface.Flush(true, true);
        RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
    }

    public void DrawDrawable(Drawable.Resource drawable)
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        using var node = new DrawableRenderNode(drawable);
        using var context = new GraphicsContext2D(node, LogicalSize, _currentDensity);
        drawable.GetOriginal().Render(context, drawable);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = Intent,
                    OutputScale = _currentDensity,
                    MaxWorkingScale = MaxWorkingScale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                },
            });
        renderer.Render(this);
    }

    public void DrawNode(RenderNode node)
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = Intent,
                    OutputScale = _currentDensity,
                    MaxWorkingScale = MaxWorkingScale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                },
            });
        renderer.Render(this);
    }

    public void DrawBackdrop(IBackdrop backdrop)
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        backdrop.Draw(this);
    }

    public IBackdrop Snapshot()
    {
        VerifyAccess();
        VerifyNestedExecutionOperation();
        // Use SurfaceDensity (not Density, which PushDeviceSpace lowers to 1) so the backdrop un-scales correctly.
        return new TmpBackdrop(_renderTarget.Snapshot(), SurfaceDensity);
    }

    public void DrawBitmap(Bitmap bmp, Brush.Resource? fill, Pen.Resource? pen)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);

        if (bmp.ByteCount <= 0)
            return;

        VerifyPixelOperation();
        VerifyCallbackResource(bmp, nameof(bmp));
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        var size = new Size(bmp.Width, bmp.Height);
        ConfigureFillPaint(new(size), fill);

        using var img = SKImage.FromBitmap(bmp.SKBitmap);

        RecordPixelOperation();
        Canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    public void DrawBitmap(Bitmap bmp, LoweredBrush fill, LoweredPen pen)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);
        if (bmp.ByteCount <= 0)
            return;

        VerifyPixelOperation();
        VerifyCallbackResource(bmp, nameof(bmp));
        VerifyLoweredPaint(fill, pen);
        ConfigureFillPaint(new Rect(new Size(bmp.Width, bmp.Height)), fill);
        using var image = SKImage.FromBitmap(bmp.SKBitmap);
        RecordPixelOperation();
        Canvas.DrawImage(
            image,
            0,
            0,
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            _sharedFillPaint);
    }

    // Draw a bitmap into a logical destination rect (Mitchell resample).
    public void DrawBitmapScaled(Bitmap bmp, Rect dest, Brush.Resource? fill)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);

        if (bmp.ByteCount <= 0)
            return;

        VerifyPixelOperation();
        VerifyCallbackResource(bmp, nameof(bmp));
        VerifyCallbackResource(fill, nameof(fill));
        ConfigureFillPaint(new(dest.Size), fill);

        using var img = SKImage.FromBitmap(bmp.SKBitmap);
        var src = SKRect.Create(bmp.Width, bmp.Height);

        RecordPixelOperation();
        Canvas.DrawImage(img, src, dest.ToSKRect(), new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    public void DrawBitmapScaled(Bitmap bmp, Rect dest, LoweredBrush fill)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);
        if (bmp.ByteCount <= 0)
            return;

        VerifyPixelOperation();
        VerifyCallbackResource(bmp, nameof(bmp));
        VerifyLoweredBrush(fill, nameof(fill));
        ConfigureFillPaint(new Rect(dest.Size), fill);
        using var image = SKImage.FromBitmap(bmp.SKBitmap);
        var source = SKRect.Create(bmp.Width, bmp.Height);
        RecordPixelOperation();
        Canvas.DrawImage(
            image,
            source,
            dest.ToSKRect(),
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            _sharedFillPaint);
    }

    public void DrawImageSource(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        if (_executionToken is null)
            VerifyNestedExecutionOperation();
        else
            VerifyCallbackResource(source, nameof(source));
        var bitmap = source.Bitmap;
        if (bitmap != null)
        {
            if (_executionToken is null)
                DrawBitmap(bitmap, fill, pen);
            else
                _executionToken.AuthorizeResource(bitmap, () => DrawBitmap(bitmap, fill, pen));
        }
    }

    public void DrawImageSource(ImageSource.Resource source, LoweredBrush fill, LoweredPen pen)
    {
        VerifyAccess();
        VerifyExecutionScope(nameof(DrawImageSource));
        VerifyCallbackResource(source, nameof(source));
        VerifyLoweredPaint(fill, pen);
        if (source.Bitmap is { } bitmap)
            _executionToken!.AuthorizeResource(bitmap, () => DrawBitmap(bitmap, fill, pen));
    }

    public void DrawVideoSource(VideoSource.Resource source, TimeSpan frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        if (_executionToken is null)
            VerifyNestedExecutionOperation();
        else
            VerifyCallbackResource(source, nameof(source));
        Rational rate = source.FrameRate;
        double frameNum = frame.TotalSeconds * (rate.Numerator / (double)rate.Denominator);
        DrawVideoSource(source, (int)frameNum, fill, pen);
    }

    public void DrawVideoSource(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        if (_executionToken is null)
            VerifyNestedExecutionOperation();
        else
            VerifyCallbackResource(source, nameof(source));
        if (source.Read(frame, out var bitmapRef))
        {
            using (bitmapRef)
            {
                void DrawFrame()
                {
                    if (source.ProxyResolution == null)
                    {
                        DrawBitmap(bitmapRef.Value, fill, pen);
                    }
                    else
                    {
                        var dest = new Rect(default, source.LogicalFrameSize.ToSize(1));
                        DrawBitmapScaled(bitmapRef.Value, dest, fill);
                    }
                }

                if (_executionToken is null)
                    DrawFrame();
                else
                    _executionToken.AuthorizeResource(bitmapRef.Value, DrawFrame);
            }
        }
    }

    public void DrawVideoSource(
        VideoSource.Resource source,
        int frame,
        LoweredBrush fill,
        LoweredPen pen)
    {
        VerifyAccess();
        VerifyExecutionScope(nameof(DrawVideoSource));
        VerifyCallbackResource(source, nameof(source));
        VerifyLoweredPaint(fill, pen);
        if (!source.Read(frame, out var bitmapRef))
            return;

        using (bitmapRef)
        {
            _executionToken!.AuthorizeResource(
                bitmapRef.Value,
                () =>
                {
                    if (source.ProxyResolution is null)
                    {
                        DrawBitmap(bitmapRef.Value, fill, pen);
                    }
                    else
                    {
                        var destination = new Rect(default, source.LogicalFrameSize.ToSize(1));
                        DrawBitmapScaled(bitmapRef.Value, destination, fill);
                    }
                });
        }
    }

    public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        ConfigureFillPaint(rect, fill);
        RecordPixelOperation();
        Canvas.DrawOval(rect.ToSKRect(), _sharedFillPaint);

        if (pen != null && pen.Thickness != 0)
        {
            using (var path = new SKPath())
            {
                path.AddOval(rect.ToSKRect());
                DrawSKPath(path, true, fill, pen);
            }
        }
    }

    public void DrawEllipse(Rect rect, LoweredBrush fill, LoweredPen pen)
    {
        VerifyPixelOperation();
        VerifyLoweredPaint(fill, pen);
        ConfigureFillPaint(rect, fill);
        RecordPixelOperation();
        Canvas.DrawOval(rect.ToSKRect(), _sharedFillPaint);

        if (pen.Resource is { Thickness: not 0 })
        {
            using var path = new SKPath();
            path.AddOval(rect.ToSKRect());
            DrawSKPath(path, true, fill, pen);
        }
    }

    public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        ConfigureFillPaint(rect, fill);
        RecordPixelOperation();
        Canvas.DrawRect(rect.ToSKRect(), _sharedFillPaint);

        if (pen != null && pen.Thickness != 0)
        {
            using (var path = new SKPath())
            {
                path.AddRect(rect.ToSKRect());
                DrawSKPath(path, true, fill, pen);
            }
        }
    }

    public void DrawRectangle(Rect rect, LoweredBrush fill, LoweredPen pen)
    {
        VerifyPixelOperation();
        VerifyLoweredPaint(fill, pen);
        ConfigureFillPaint(rect, fill);
        RecordPixelOperation();
        Canvas.DrawRect(rect.ToSKRect(), _sharedFillPaint);

        if (pen.Resource is { Thickness: not 0 })
        {
            using var path = new SKPath();
            path.AddRect(rect.ToSKRect());
            DrawSKPath(path, true, fill, pen);
        }
    }

    public void DrawText(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(text, nameof(text));
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        float density = _currentDensity;
        SKTextBlob? textBlob = text.GetTextBlob(density);
        if (textBlob is null)
        {
            // Empty text shapes to no glyphs, so there is nothing to fill or stroke.
            return;
        }

        if (density == 1f)
        {
            ConfigureFillPaint(text.Bounds, fill);
            RecordPixelOperation();
            Canvas.DrawText(textBlob, 0, 0, _sharedFillPaint);

            if (pen != null
                && pen.Thickness > 0
                && text.GetStrokePath() is { } stroke)
            {
                ConfigureStrokePaint(new(text.Bounds.Size), pen);
                Canvas.DrawPath(stroke, _sharedStrokePaint);
            }
        }
        else
        {
            int count = Canvas.Save();
            try
            {
                Canvas.SetMatrix((SKMatrix44)CreateDensityScaledContentTransform(density).ToSKMatrix());

                // The blob is shaped at device density, so its glyphs already span Bounds * density
                // under this CTM. Pass scale 1 so the density isn't applied twice to brush patterns.
                ConfigureFillPaint(text.Bounds * density, fill, scale: 1f);
                RecordPixelOperation();
                Canvas.DrawText(textBlob, 0, 0, _sharedFillPaint);

                if (pen != null
                    && pen.Thickness > 0
                    && text.GetStrokePath(density) is { } stroke)
                {
                    ConfigureStrokePaint(new(text.Bounds.Size * density), pen, scale: 1f);
                    Canvas.DrawPath(stroke, _sharedStrokePaint);
                }
            }
            finally
            {
                Canvas.RestoreToCount(count);
            }
        }
    }

    public void DrawText(FormattedText text, LoweredBrush fill, LoweredPen pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(text, nameof(text));
        VerifyLoweredPaint(fill, pen);
        float density = _currentDensity;
        SKTextBlob? textBlob = text.GetTextBlob(density);
        if (textBlob is null)
            return;

        if (density == 1f)
        {
            ConfigureFillPaint(text.Bounds, fill);
            RecordPixelOperation();
            Canvas.DrawText(textBlob, 0, 0, _sharedFillPaint);
            if (pen.Resource is { Thickness: > 0 }
                && text.GetStrokePath() is { } stroke)
            {
                ConfigureStrokePaint(new Rect(text.Bounds.Size), pen);
                Canvas.DrawPath(stroke, _sharedStrokePaint);
            }
        }
        else
        {
            int count = Canvas.Save();
            try
            {
                Canvas.SetMatrix((SKMatrix44)CreateDensityScaledContentTransform(density).ToSKMatrix());
                ConfigureFillPaint(text.Bounds * density, fill, scale: 1f);
                RecordPixelOperation();
                Canvas.DrawText(textBlob, 0, 0, _sharedFillPaint);
                if (pen.Resource is { Thickness: > 0 }
                    && text.GetStrokePath(density) is { } stroke)
                {
                    ConfigureStrokePaint(new Rect(text.Bounds.Size * density), pen, scale: 1f);
                    Canvas.DrawPath(stroke, _sharedStrokePaint);
                }
            }
            finally
            {
                Canvas.RestoreToCount(count);
            }
        }
    }

    private Matrix CreateDensityScaledContentTransform(float density)
    {
        if (density == 1f || _currentBaseTransform.IsIdentity)
        {
            return _currentTransform;
        }

        if (!_currentBaseTransform.TryInvert(out Matrix inverseBase))
        {
            return _currentTransform;
        }

        Matrix logicalTransform = _currentTransform.Append(inverseBase);
        return Matrix.CreateScale(1f / density, 1f / density)
            .Append(logicalTransform)
            .Append(_currentBaseTransform);
    }

    internal void DrawSKPath(SKPath skPath, bool strokeOnly, Brush.Resource? fill, Pen.Resource? pen)
    {
        Rect rect = skPath.Bounds.ToGraphicsRect();

        if (!strokeOnly)
        {
            ConfigureFillPaint(rect, fill);
            RecordPixelOperation();
            Canvas.DrawPath(skPath, _sharedFillPaint);
        }

        if (pen != null && pen.Thickness > 0)
        {
            ConfigureStrokePaint(rect, pen);

            using SKPath strokePath = PenHelper.CreateStrokePath(skPath, pen, rect);
            RecordPixelOperation();
            Canvas.DrawPath(strokePath, _sharedStrokePaint);
        }
    }

    internal void DrawSKPath(SKPath skPath, bool strokeOnly, LoweredBrush fill, LoweredPen pen)
    {
        Rect rect = skPath.Bounds.ToGraphicsRect();
        if (!strokeOnly)
        {
            ConfigureFillPaint(rect, fill);
            RecordPixelOperation();
            Canvas.DrawPath(skPath, _sharedFillPaint);
        }

        if (pen.Resource is { Thickness: > 0 } resource)
        {
            ConfigureStrokePaint(rect, pen);
            using SKPath strokePath = PenHelper.CreateStrokePath(skPath, resource, rect);
            RecordPixelOperation();
            Canvas.DrawPath(strokePath, _sharedStrokePaint);
        }
    }

    public void DrawGeometry(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(geometry, nameof(geometry));
        VerifyCallbackResource(fill, nameof(fill));
        VerifyCallbackResource(pen, nameof(pen));
        SKPath skPath = geometry.GetCachedPath();
        Rect rect = geometry.Bounds;

        ConfigureFillPaint(geometry.Bounds, fill);
        RecordPixelOperation();
        if (!TryDrawProductCoverageRectangle(geometry, _sharedFillPaint))
            Canvas.DrawPath(skPath, _sharedFillPaint);

        if (pen != null && pen.Thickness > 0)
        {
            ConfigureStrokePaint(rect, pen);
            SKPath? stroke = geometry.GetCachedStrokePath(pen);
            if (stroke != null)
            {
                Canvas.DrawPath(stroke, _sharedStrokePaint);
            }
        }
    }

    public void DrawGeometry(Geometry.Resource geometry, LoweredBrush fill, LoweredPen pen)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(geometry, nameof(geometry));
        VerifyLoweredPaint(fill, pen);
        SKPath path = geometry.GetCachedPath();
        Rect rect = geometry.Bounds;
        ConfigureFillPaint(rect, fill);
        RecordPixelOperation();
        if (!TryDrawProductCoverageRectangle(geometry, _sharedFillPaint))
            Canvas.DrawPath(path, _sharedFillPaint);

        if (pen.Resource is { Thickness: > 0 } resource)
        {
            ConfigureStrokePaint(rect, pen);
            if (geometry.GetCachedStrokePath(resource) is { } stroke)
                Canvas.DrawPath(stroke, _sharedStrokePaint);
        }
    }

    public void Pop(int count = -1)
    {
        VerifyAccess();
        int stateFloor = _executionToken is null ? 0 : _callbackStateFloor;

        if (count < 0)
        {
            while (_states.Count > stateFloor
                   && count < 0
                   && _states.TryPop(out CanvasPushedState? state))
            {
                state.Pop(this);
                count++;
            }
        }
        else
        {
            while (_states.Count > stateFloor
                   && _states.Count >= count
                   && _states.TryPop(out CanvasPushedState? state))
            {
                state.Pop(this);
            }
        }
    }

    public PushedState Push()
    {
        VerifyAccess();
        int count = Canvas.Save();

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushLayer(Rect limit = default)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        int count;
        if (limit == default)
        {
            RecordPixelOperation();
            count = Canvas.SaveLayer();
        }
        else
        {
            using (var paint = new SKPaint())
            {
                RecordPixelOperation();
                count = Canvas.SaveLayer(limit.ToSKRect(), paint);
            }
        }

        _states.Push(new CanvasPushedState.LayerPushedState(count));
        return new PushedState(this, _states.Count);
    }

    internal PushedState PushPaint(SKPaint paint, Rect? rect = null)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        int count;
        if (rect.HasValue)
        {
            RecordPixelOperation();
            count = Canvas.SaveLayer(rect.Value.ToSKRect(), paint);
        }
        else
        {
            RecordPixelOperation();
            count = Canvas.SaveLayer(paint);
        }

        _states.Push(new CanvasPushedState.LayerPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        int count = Canvas.Save();
        var replay = new RectClipReplayOperation(clip, operation, Transform);
        ClipRect(clip, operation);

        _clipReplayOperations.Push(replay);
        _states.Push(new CanvasPushedState.ClipPushedState(count, replay));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushClip(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        VerifyCallbackResource(geometry, nameof(geometry));
        int count = Canvas.Save();
        var replay = new PathClipReplayOperation(
            new SKPath(geometry.GetCachedPath()),
            operation,
            Transform);
        ClipPath(geometry, operation);

        _clipReplayOperations.Push(replay);
        _states.Push(new CanvasPushedState.ClipPushedState(count, replay));
        return new PushedState(this, _states.Count);
    }

    internal void ReplayClipTo(ImmediateCanvas destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        VerifyAccess();
        destination.VerifyAccess();

        Matrix finalTransform = destination.Transform;
        foreach (ClipReplayOperation operation in _clipReplayOperations.Reverse())
        {
            destination.Transform = operation.Transform;
            operation.Apply(destination.Canvas);
        }

        // Include clips applied directly through the Skia canvas or ClipRect/ClipPath without a
        // replayable PushClip entry. Their exact path is not observable, but their device bounds
        // still prevent expanded execution from escaping the destination's available clip domain.
        SKRectI deviceClip = Canvas.DeviceClipBounds;
        destination.Transform = Matrix.Identity;
        destination.Canvas.ClipRect(deviceClip);
        destination.Transform = finalTransform;
    }

    private void PopClipReplay(ClipReplayOperation operation)
    {
        if (!_clipReplayOperations.TryPop(out ClipReplayOperation? active)
            || !ReferenceEquals(active, operation))
        {
            throw new InvalidOperationException("The canvas clip replay stack is unbalanced.");
        }

        operation.Dispose();
    }

    private abstract class ClipReplayOperation(Matrix transform) : IDisposable
    {
        public Matrix Transform { get; } = transform;

        public abstract void Apply(SKCanvas canvas);

        public virtual void Dispose()
        {
        }
    }

    private sealed class RectClipReplayOperation(
        Rect clip,
        ClipOperation operation,
        Matrix transform) : ClipReplayOperation(transform)
    {
        public override void Apply(SKCanvas canvas)
            => canvas.ClipRect(clip.ToSKRect(), operation.ToSKClipOperation());
    }

    private sealed class PathClipReplayOperation(
        SKPath path,
        ClipOperation operation,
        Matrix transform) : ClipReplayOperation(transform)
    {
        public override void Apply(SKCanvas canvas)
            => canvas.ClipPath(path, operation.ToSKClipOperation(), antialias: true);

        public override void Dispose()
            => path.Dispose();
    }

    public PushedState PushOpacity(float opacity)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        float oldOpacity = Opacity;
        Opacity *= opacity;

        RecordPixelOperation();
        if (oldOpacity == 1f && opacity == 1f)
        {
            // Skia sizes an isolation layer from the active clip, and rasterizing into that smaller
            // surface changes antialiased coverage. A fully opaque group is SrcOver-associative, so
            // the layer would only be an identity pass that perturbs coverage.
            _states.Push(new CanvasPushedState.SKCanvasPushedState(Canvas.Save()));
            return new PushedState(this, _states.Count);
        }

        var paint = new SKPaint();
        int count = Canvas.SaveLayer(paint);
        paint.Color = new SKColor(0, 0, 0, (byte)(Opacity * 255));
        _states.Push(new CanvasPushedState.OpacityPushedState(oldOpacity, count, paint));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        var paint = new SKPaint();

        RecordPixelOperation();
        int count = Canvas.SaveLayer(paint);
        new BrushConstructor(
            bounds,
            mask,
            (BlendMode)paint.BlendMode,
            _currentDensity,
            MaxWorkingScale,
            Intent,
            DrawableBrushMaterializer).ConfigurePaint(paint);
        _states.Push(new CanvasPushedState.MaskPushedState(count, invert, paint));
        return new PushedState(this, _states.Count);
    }

    /// <remarks>
    /// This stays internal while its sibling draw overloads are public: it is SaveLayer-backed, and
    /// <see cref="VerifyHiddenLayerOperation"/> rejects SaveLayer-backed state on every guarded callback
    /// canvas, which is the only kind an author can reach. Its callers are executor replays onto their own
    /// unguarded target.
    /// </remarks>
    internal PushedState PushOpacityMask(LoweredBrush mask, Rect bounds, bool invert = false)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        VerifyLoweredBrush(mask, nameof(mask));
        var paint = new SKPaint();
        RecordPixelOperation();
        int count = Canvas.SaveLayer(paint);
        new BrushConstructor(
            bounds,
            mask,
            (BlendMode)paint.BlendMode,
            _currentDensity,
            MaxWorkingScale,
            Intent,
            DrawableBrushMaterializer).ConfigurePaint(paint);
        _states.Push(new CanvasPushedState.MaskPushedState(count, invert, paint));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
    {
        VerifyAccess();
        int count = Canvas.Save();

        if (transformOperator == TransformOperator.Prepend)
        {
            Transform = Transform.Prepend(matrix);
        }
        else if (transformOperator == TransformOperator.Append)
        {
            Transform = Transform.Append(matrix);
        }
        else
        {
            // Set re-applies the current base CTM so the canvas stays in the right coordinate space.
            Transform = _currentBaseTransform.Prepend(matrix);
        }

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    /// <summary>
    /// Enter absolute device space (CTM = identity, <see cref="Density"/> = 1) for the lifetime
    /// of the returned state. Restored on dispose.
    /// </summary>
    public PushedState PushDeviceSpace()
    {
        VerifyAccess();

        // No-op when already in absolute device space.
        if (_currentDensity == 1f && _currentTransform.IsIdentity && _currentBaseTransform.IsIdentity)
        {
            _states.Push(CanvasPushedState.NoOpPushedState.Instance);
            return new PushedState(this, _states.Count);
        }

        int count = Canvas.Save();
        var state = new CanvasPushedState.DeviceSpacePushedState(count, _currentDensity, _currentBaseTransform);

        Canvas.SetMatrix((SKMatrix44)Matrix.Identity.ToSKMatrix());
        _currentTransform = Matrix.Identity;
        _currentDensity = 1f;
        _currentBaseTransform = Matrix.Identity;

        _states.Push(state);
        return new PushedState(this, _states.Count);
    }

    public PushedState PushBlendMode(BlendMode blendMode)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        BlendMode tmp = BlendMode;
        bool previousProductRectangleCoverage = _productRectangleCoverage;
        BlendMode = blendMode;
        _productRectangleCoverage = blendMode == BlendMode.DstIn;
        var paint = new SKPaint();
        paint.BlendMode = (SKBlendMode)blendMode;

        RecordPixelOperation();
        int count = Canvas.SaveLayer(paint);
        _states.Push(new CanvasPushedState.BlendModePushedState(
            tmp,
            previousProductRectangleCoverage,
            count,
            paint));
        return new PushedState(this, _states.Count);
    }

    internal PushedState PushDirectBlendMode(BlendMode blendMode)
    {
        VerifyAccess();
        int count = Canvas.Save();
        BlendMode previousBlendMode = BlendMode;
        BlendMode? previousDirectBlendMode = _directBlendMode;
        BlendMode = blendMode;
        _directBlendMode = blendMode;
        _states.Push(new CanvasPushedState.DirectBlendModePushedState(
            previousBlendMode,
            previousDirectBlendMode,
            count));
        return new PushedState(this, _states.Count);
    }

    internal void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        _dispatcher?.VerifyAccess();
        if (_executionToken is not null && !_executionToken.IsActiveCanvas(this))
            throw new InvalidOperationException("The executor-managed callback canvas is no longer active.");
    }

    internal void ConfigureExecutionCallback(
        RenderExecutionSessionToken token,
        CallbackCanvasCapability capability)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (_executionToken is not null)
            throw new InvalidOperationException("The canvas already has an execution capability.");
        if (!token.IsActiveCanvas(this))
            throw new InvalidOperationException("The canvas must be active before a capability is attached.");

        _executionToken = token;
        _callbackCapability = capability;
    }

    /// <summary>
    /// Attaches the guarded draw capability to this canvas for the duration of a direct replay, then detaches
    /// it again.
    /// </summary>
    /// <remarks>
    /// A direct replay writes onto a canvas the executor keeps using afterwards, so the capability cannot be
    /// ended by <see cref="CloseWithoutFlush"/> the way an execution view's is. Transform and clip are left
    /// untouched: attaching the guard must not change a single pixel of what the replay draws.
    /// </remarks>
    internal DirectExecutionScope BeginDirectExecution(RenderExecutionSessionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        VerifyAccess();
        int canvasSaveCount = Canvas.Save();
        var outer = new DirectExecutionScope(
            this,
            token,
            _executionToken,
            _callbackCapability,
            _callbackStateFloor,
            _isReplayingTargetScope,
            canvasSaveCount);
        try
        {
            token.EnterCanvas(this, facade: null);
            _executionToken = token;
            _callbackCapability = CallbackCanvasCapability.Draw;
            _callbackStateFloor = _states.Count;
            _isReplayingTargetScope = false;
            return outer;
        }
        catch
        {
            Canvas.RestoreToCount(canvasSaveCount);
            throw;
        }
    }

    private void EndDirectExecution(
        RenderExecutionSessionToken token,
        RenderExecutionSessionToken? outerToken,
        CallbackCanvasCapability? outerCapability,
        int outerStateFloor,
        bool outerIsReplayingTargetScope,
        int canvasSaveCount)
    {
        try
        {
            // The destination outlives the replay, so state the callback left pushed has to be unwound here;
            // an execution view gets the same treatment from CloseWithoutFlush.
            while (_states.Count > _callbackStateFloor && _states.TryPop(out CanvasPushedState? state))
                state.Pop(this);
        }
        finally
        {
            try
            {
                Canvas.RestoreToCount(canvasSaveCount);
            }
            finally
            {
                _executionToken = outerToken;
                _callbackCapability = outerCapability;
                _callbackStateFloor = outerStateFloor;
                _isReplayingTargetScope = outerIsReplayingTargetScope;
                token.ExitCanvas(this);
            }
        }
    }

    internal readonly struct DirectExecutionScope(
        ImmediateCanvas canvas,
        RenderExecutionSessionToken token,
        RenderExecutionSessionToken? outerToken,
        CallbackCanvasCapability? outerCapability,
        int outerStateFloor,
        bool outerIsReplayingTargetScope,
        int canvasSaveCount) : IDisposable
    {
        public void Dispose() => canvas.EndDirectExecution(
            token,
            outerToken,
            outerCapability,
            outerStateFloor,
            outerIsReplayingTargetScope,
            canvasSaveCount);
    }

    internal ImmediateCanvas CreateExecutionView()
    {
        VerifyAccess();
        if (_executionToken is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "An executor-managed callback canvas cannot create another execution view.");
        }

        return new ImmediateCanvas(this);
    }

    internal void ConfigureRawExecutionCallback(RenderExecutionSessionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (_executionToken is not null)
            throw new InvalidOperationException("The canvas already has an execution capability.");
        if (!token.IsActiveCanvas(this))
            throw new InvalidOperationException("The canvas must be active before a capability is attached.");

        _executionToken = token;
        _callbackCapability = null;
        _callbackStateFloor = _states.Count;
    }

    internal void PinExecutionCallbackState()
    {
        VerifyAccess();
        if (_executionToken is null)
            throw new InvalidOperationException("Only an execution callback canvas can pin its base state.");

        _callbackStateFloor = _states.Count;
    }

    internal void CloseWithoutFlush()
    {
        if (IsDisposed)
            return;

        if (_dispatcher == null)
        {
            CloseCore(flush: false);
        }
        else
        {
            _dispatcher.Invoke(() => CloseCore(flush: false));
        }
    }

    internal void DrawExecutionInput(SKImage image, Rect destination)
    {
        ArgumentNullException.ThrowIfNull(image);
        VerifyPixelOperation();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;
        RecordPixelOperation();
        Canvas.DrawImage(
            image,
            SKRect.Create(image.Width, image.Height),
            destination.ToSKRect(),
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            _sharedFillPaint);
    }

    internal void DrawExecutionInputDeviceSpace(SKImage image, Point localDevicePoint)
    {
        ArgumentNullException.ThrowIfNull(image);
        VerifyPixelOperation();
        using (PushDeviceSpace())
        {
            _sharedFillPaint.Reset();
            ApplyDirectBlendMode(_sharedFillPaint);
            _sharedFillPaint.IsAntialias = true;
            RecordPixelOperation();
            Canvas.DrawImage(
                image,
                localDevicePoint.X,
                localDevicePoint.Y,
                new SKSamplingOptions(SKCubicResampler.Mitchell),
                _sharedFillPaint);
        }
    }

    /// <summary>
    /// Replays a target scope's recorded input, permitting nested render work for the replay's duration.
    /// </summary>
    internal void ReplayTargetScopeInput(Action<ImmediateCanvas> replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        VerifyAccess();
        if (_executionToken is null
            || (_callbackCapability is not null and not CallbackCanvasCapability.TargetScope)
            || _isReplayingTargetScope)
        {
            throw new InvalidOperationException("A target-scope replay is not active for this canvas.");
        }

        _isReplayingTargetScope = true;
        try
        {
            replay(this);
        }
        finally
        {
            _isReplayingTargetScope = false;
        }
    }

    private void CloseCore(bool flush)
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
            _sharedFillPaint.Dispose();
            _sharedStrokePaint.Dispose();
        }
    }

    internal static void RecordFlush(ImmediateCanvasFlushKind kind)
    {
        for (FlushObserverScope? scope = s_flushObserver.Value; scope is not null; scope = scope.Parent)
        {
            try
            {
                scope.Observer(kind);
            }
            catch
            {
                // Test observation must never affect rendering or cleanup.
            }
        }
    }

    private static void RecordPixelOperation()
    {
        for (PixelOperationObserverScope? scope = s_pixelOperationObserver.Value;
             scope is not null;
             scope = scope.Parent)
        {
            try
            {
                scope.Observer();
            }
            catch
            {
                // Diagnostics observation must never affect rendering or cleanup.
            }
        }
    }

    private sealed class FlushObserverScope(
        FlushObserverScope? parent,
        Action<ImmediateCanvasFlushKind> observer) : IDisposable
    {
        private bool _disposed;

        public FlushObserverScope? Parent { get; } = parent;

        public Action<ImmediateCanvasFlushKind> Observer { get; } = observer;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!ReferenceEquals(s_flushObserver.Value, this))
                throw new InvalidOperationException("Immediate-canvas flush observers must be closed in LIFO order.");
            s_flushObserver.Value = Parent;
        }
    }

    private sealed class PixelOperationObserverScope(
        PixelOperationObserverScope? parent,
        Action observer) : IDisposable
    {
        private bool _disposed;

        public PixelOperationObserverScope? Parent { get; } = parent;

        public Action Observer { get; } = observer;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!ReferenceEquals(s_pixelOperationObserver.Value, this))
                throw new InvalidOperationException("Immediate-canvas pixel-operation observers must be closed in LIFO order.");
            s_pixelOperationObserver.Value = Parent;
        }
    }

    private void VerifyPixelOperation(bool isClear = false)
    {
        VerifyAccess();
        switch (_callbackCapability)
        {
            case CallbackCanvasCapability.TargetScope when !_isReplayingTargetScope:
                throw new InvalidOperationException(
                    "A target-scope callback may only surround ReplayInput with transform and clip state.");
            case CallbackCanvasCapability.TargetCommandEmpty:
                throw new InvalidOperationException("An empty target command cannot perform pixel operations.");
            case CallbackCanvasCapability.TargetCommandRegion when isClear:
                throw new InvalidOperationException(
                    "The native clear operation is valid only for a full target command region.");
        }
    }

    private void VerifyHiddenLayerOperation()
    {
        if (_callbackCapability is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "SaveLayer-backed state is not available on a guarded callback canvas.");
        }
    }

    private void VerifyNestedExecutionOperation()
    {
        if (_callbackCapability is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "Nested render work, snapshots, and legacy raw callbacks are not available on a guarded callback canvas.");
        }
    }

    private void VerifyNativeTargetOperation()
    {
        if (_callbackCapability is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "Raw surfaces and render targets are not available on a guarded callback canvas.");
        }
    }

    private void VerifyCallbackResource(object? resource, string parameterName)
    {
        if (resource is null
            || _executionToken is null
            || _callbackCapability is null
            || _isReplayingTargetScope)
            return;

        if (!_executionToken.IsResourceAuthorized(resource))
        {
            throw new InvalidOperationException(
                $"The resource passed as '{parameterName}' is not authorized in the active execution scope.");
        }
    }

    private void VerifyLoweredPaint(LoweredBrush fill, LoweredPen pen)
    {
        VerifyLoweredBrush(fill, nameof(fill));
        VerifyLease(pen.IsLeaseActive, nameof(pen));
        VerifyCallbackResource(pen.Resource, nameof(pen));
        VerifyCallbackResource(pen.Brush.Resource, nameof(pen));
        VerifyCallbackResource(pen.Brush.TileContent?.Shader, nameof(pen));
    }

    private void VerifyLoweredBrush(LoweredBrush brush, string parameterName)
    {
        VerifyLease(brush.IsLeaseActive, parameterName);
        VerifyCallbackResource(brush.Resource, parameterName);
        VerifyCallbackResource(brush.TileContent?.Shader, parameterName);
    }

    /// <remarks>
    /// Unlike <see cref="VerifyCallbackResource"/> this asks the execution that resolved the paint, not this
    /// canvas, so it also rejects a copy handed to a canvas the author owns.
    /// </remarks>
    private static void VerifyLease(bool isLeaseActive, string parameterName)
    {
        if (!isLeaseActive)
        {
            throw new InvalidOperationException(
                $"The lowered paint passed as '{parameterName}' is outside the lease of the execution that "
                + "resolved it.");
        }
    }

    private void VerifyExecutionScope(string operationName)
    {
        if (_executionToken is null)
        {
            throw new InvalidOperationException(
                $"{operationName} under a lowered paint is only available inside a render execution.");
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
                DrawableBrushMaterializer).ConfigurePaint(_sharedStrokePaint);
        }
    }

    private void ConfigureStrokePaint(
        Rect bounds,
        LoweredPen pen,
        BlendMode blendMode = BlendMode.SrcOver,
        float? scale = null)
    {
        _sharedStrokePaint.Reset();
        if (pen.Resource is { Thickness: not 0 })
        {
            _sharedStrokePaint.IsStroke = false;
            new BrushConstructor(
                bounds,
                pen.Brush,
                ResolvePaintBlendMode(blendMode),
                scale ?? _currentDensity,
                MaxWorkingScale,
                Intent,
                DrawableBrushMaterializer).ConfigurePaint(_sharedStrokePaint);
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
            DrawableBrushMaterializer).ConfigurePaint(_sharedFillPaint);
    }

    private void ConfigureFillPaint(
        Rect bounds,
        LoweredBrush brush,
        BlendMode blendMode = BlendMode.SrcOver,
        float? scale = null)
    {
        _sharedFillPaint.Reset();
        new BrushConstructor(
            bounds,
            brush,
            ResolvePaintBlendMode(blendMode),
            scale ?? _currentDensity,
            MaxWorkingScale,
            Intent,
            DrawableBrushMaterializer).ConfigurePaint(_sharedFillPaint);
    }

    private BlendMode ResolvePaintBlendMode(BlendMode fallback)
        => _directBlendMode ?? fallback;

    private void ApplyDirectBlendMode(SKPaint paint)
    {
        if (_directBlendMode is { } blendMode)
            paint.BlendMode = (SKBlendMode)blendMode;
    }

    private bool TryDrawProductCoverageRectangle(Geometry.Resource geometry, SKPaint paint)
    {
        // Skia's path antialiasing may publish one-axis coverage at a fractional rectangle corner.
        // A Porter-Duff mask needs the geometric area product, because the same coverage is later
        // applied to every pixel in the isolated target-layer domain.
        if ((_directBlendMode != BlendMode.DstOut && !_productRectangleCoverage)
            || geometry.GetOriginal() is not RectGeometry
            || _currentTransform.M12 != 0
            || _currentTransform.M13 != 0
            || _currentTransform.M21 != 0
            || _currentTransform.M23 != 0
            || _currentTransform.M33 != 1)
        {
            return false;
        }

        var rectangle = (RectGeometry.Resource)geometry;
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return true;
        }

        SKColor previousColor = paint.Color;
        SKShader? previousShader = paint.Shader;
        using SKShader? ownedSourceShader = previousShader is null
            ? SKShader.CreateColor(new SKColor(
                previousColor.Red,
                previousColor.Green,
                previousColor.Blue,
                255))
            : null;
        SKShader sourceShader = previousShader ?? ownedSourceShader!;
        using var uniforms = new SKRuntimeEffectUniforms(s_rectCoverageEffect.Value);
        using var children = new SKRuntimeEffectChildren(s_rectCoverageEffect.Value);
        uniforms["left"] = (float)geometry.Bounds.Left;
        uniforms["top"] = (float)geometry.Bounds.Top;
        uniforms["right"] = (float)geometry.Bounds.Right;
        uniforms["bottom"] = (float)geometry.Bounds.Bottom;
        uniforms["scaleX"] = MathF.Abs(_currentTransform.M11);
        uniforms["scaleY"] = MathF.Abs(_currentTransform.M22);
        children["src"] = sourceShader;
        using SKShader coverageShader = s_rectCoverageEffect.Value.ToShader(uniforms, children);
        bool previousAntialias = paint.IsAntialias;
        try
        {
            paint.Color = new SKColor(255, 255, 255, previousColor.Alpha);
            paint.Shader = coverageShader;
            paint.IsAntialias = false;
            Canvas.DrawPaint(paint);
        }
        finally
        {
            paint.Shader = previousShader;
            paint.Color = previousColor;
            paint.IsAntialias = previousAntialias;
        }

        return true;
    }

    private static SKRuntimeEffect CreateRectCoverageEffect()
    {
        const string source =
            """
            uniform shader src;
            uniform float left;
            uniform float top;
            uniform float right;
            uniform float bottom;
            uniform float scaleX;
            uniform float scaleY;

            half4 main(float2 p)
            {
                float x = clamp((p.x - left) * scaleX + 0.5, 0.0, 1.0)
                    * clamp((right - p.x) * scaleX + 0.5, 0.0, 1.0);
                float y = clamp((p.y - top) * scaleY + 0.5, 0.0, 1.0)
                    * clamp((bottom - p.y) * scaleY + 0.5, 0.0, 1.0);
                return src.eval(p) * half(x * y);
            }
            """;
        return SKRuntimeEffect.CreateShader(source, out string? errorText)
               ?? throw new InvalidOperationException(
                   $"Failed to compile the rectangle coverage shader: {errorText}");
    }
}
