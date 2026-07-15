using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas : IDisposable, IPopable
{
    internal readonly RenderTarget _renderTarget;
    private readonly Dispatcher? _dispatcher;
    private readonly SKPaint _sharedFillPaint = new();
    private readonly SKPaint _sharedStrokePaint = new();
    private readonly Stack<CanvasPushedState> _states = new();
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

    public ImmediateCanvas(RenderTarget renderTarget, RenderIntent renderIntent, float density = 1f,
        float maxWorkingScale = float.PositiveInfinity, Size logicalSize = default,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        RenderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        PullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        if (density <= 0f || !float.IsFinite(density))
            throw new ArgumentOutOfRangeException(nameof(density), density,
                "Density must be a positive finite value.");

        _dispatcher = Dispatcher.Current;
        _renderTarget = renderTarget;
        Canvas = _renderTarget.Value.Canvas;
        DeviceSize = new PixelSize(renderTarget.Width, renderTarget.Height);
        LogicalSize = logicalSize.IsDefault ? DeviceSize.ToSize(density) : logicalSize;
        SurfaceDensity = density;
        _currentDensity = density;
        MaxWorkingScale = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);
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

    ~ImmediateCanvas()
    {
        // A finalizer must never throw — an unhandled exception on the finalizer thread aborts the process.
        // Dispose can throw on a leaked / half-built canvas (dispatcher Invoke or context-lost GPU op), so the
        // GC path swallows; explicit Dispose() still surfaces errors.
        try
        {
            Dispose();
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

    /// <summary>Explicit preview/delivery failure policy forwarded into nested brush renders.</summary>
    public RenderIntent RenderIntent { get; }

    /// <summary>The pull purpose forwarded into nested drawables and brushes.</summary>
    public RenderPullPurpose PullPurpose { get; }

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

    public void Clear()
    {
        VerifyAccess();
        Canvas.Clear();
    }

    public void Clear(Color color)
    {
        VerifyAccess();
        Canvas.Clear(color.ToSKColor());
    }

    public void ClipRect(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        Canvas.ClipRect(clip.ToSKRect(), operation.ToSKClipOperation());
    }

    public void ClipPath(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        Canvas.ClipPath(geometry.GetCachedPath(), operation.ToSKClipOperation(), true);
    }

    public void Dispose()
    {
        void DisposeCore()
        {
            // Closing a canvas records no synchronization. Effect-pipeline flushes and backend transitions are
            // scheduled by PlanExecutor; root presentation/readback synchronizes at its own explicit boundary.
            IsDisposed = true;
            GC.SuppressFinalize(this);
            try
            {
                // Undo the base Save() (density != 1). Guard Canvas.Handle: SkiaSharp may have
                // zeroed it during GrContext teardown; RestoreToCount on a zero Handle SIGSEGVs.
                if (_baseSaveCount >= 0 && Canvas is not null && Canvas.Handle != IntPtr.Zero)
                {
                    Canvas.RestoreToCount(_baseSaveCount);
                }
            }
            catch
            {
                // Best-effort canvas-state cleanup; never abort disposal on a torn-down native canvas.
            }

            _sharedFillPaint.Dispose();
            _sharedStrokePaint.Dispose();
        }

        if (!IsDisposed)
        {
            if (_dispatcher == null)
            {
                DisposeCore();
            }
            else
            {
                _dispatcher?.Invoke(DisposeCore);
            }
        }
    }

    public void DrawSurface(SKSurface surface, Point point)
    {
        _sharedFillPaint.Reset();
        _sharedFillPaint.IsAntialias = true;

        Canvas.DrawSurface(surface, point.X, point.Y, _sharedFillPaint);
    }

    public void DrawRenderTarget(RenderTarget renderTarget, Point point)
    {
        renderTarget.VerifyAccess();
        _sharedFillPaint.Reset();
        _sharedFillPaint.IsAntialias = true;

        Canvas.DrawSurface(renderTarget.Value, point.X, point.Y, _sharedFillPaint);
    }

    // Draw a buffer into a logical destination rect (Mitchell resample).
    public void DrawRenderTargetScaled(RenderTarget renderTarget, Rect dest)
    {
        VerifyAccess();
        renderTarget.VerifyAccess();

        using SKImage image = renderTarget.Value.Snapshot();
        DrawImageScaled(image, dest);
    }

    // Draw a pre-snapshotted image into a logical destination rect (Mitchell resample).
    public void DrawImageScaled(SKImage image, Rect dest)
    {
        VerifyAccess();
        _sharedFillPaint.Reset();
        _sharedFillPaint.IsAntialias = true;

        var src = SKRect.Create(image.Width, image.Height);
        Canvas.DrawImage(image, src, dest.ToSKRect(), new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    // Draw a surface into its own logical footprint (pixel size / density) at the given origin.
    public void DrawSurfaceScaled(SKSurface surface, Point origin, float scale)
    {
        VerifyAccess();
        _sharedFillPaint.Reset();
        _sharedFillPaint.IsAntialias = true;

        using SKImage image = surface.Snapshot();
        var src = SKRect.Create(image.Width, image.Height);
        var dest = SKRect.Create((float)origin.X, (float)origin.Y, image.Width / scale, image.Height / scale);
        Canvas.DrawImage(image, src, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    public void DrawDrawable(Drawable.Resource drawable)
    {
        using var node = new DrawableRenderNode(drawable);
        using var context = new GraphicsContext2D(node, LogicalSize, _currentDensity);
        drawable.GetOriginal().Render(context, drawable);
        var processor = new RenderNodeProcessor(
            node, true, RenderIntent, _currentDensity, MaxWorkingScale,
            pullPurpose: PullPurpose)
        {
            RequestedBounds = new Rect(default, LogicalSize),
        };
        processor.Render(this);
    }

    public void DrawNode(RenderNode node)
    {
        var processor = new RenderNodeProcessor(
            node, true, RenderIntent, _currentDensity, MaxWorkingScale,
            pullPurpose: PullPurpose)
        {
            RequestedBounds = new Rect(default, LogicalSize),
        };
        processor.Render(this);
    }

    public void DrawBackdrop(IBackdrop backdrop)
    {
        backdrop.Draw(this);
    }

    public IBackdrop Snapshot()
    {
        // Use SurfaceDensity (not Density, which PushDeviceSpace lowers to 1) so the backdrop un-scales correctly.
        return new TmpBackdrop(_renderTarget.Snapshot(), SurfaceDensity);
    }

    public void DrawBitmap(Bitmap bmp, Brush.Resource? fill, Pen.Resource? pen)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);

        if (bmp.ByteCount <= 0)
            return;

        VerifyAccess();
        var size = new Size(bmp.Width, bmp.Height);
        ConfigureFillPaint(new(size), fill);

        using var img = SKImage.FromBitmap(bmp.SKBitmap);

        Canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    // Draw a bitmap into a logical destination rect (Mitchell resample).
    public void DrawBitmapScaled(Bitmap bmp, Rect dest, Brush.Resource? fill)
    {
        ObjectDisposedException.ThrowIf(bmp.IsDisposed, bmp);

        if (bmp.ByteCount <= 0)
            return;

        VerifyAccess();
        ConfigureFillPaint(new(dest.Size), fill);

        using var img = SKImage.FromBitmap(bmp.SKBitmap);
        var src = SKRect.Create(bmp.Width, bmp.Height);

        Canvas.DrawImage(img, src, dest.ToSKRect(), new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    public void DrawImageSource(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen)
    {
        var bitmap = source.Bitmap;
        if (bitmap != null)
        {
            DrawBitmap(bitmap, fill, pen);
        }
    }

    public void DrawVideoSource(VideoSource.Resource source, TimeSpan frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        Rational rate = source.FrameRate;
        double frameNum = frame.TotalSeconds * (rate.Numerator / (double)rate.Denominator);
        DrawVideoSource(source, (int)frameNum, fill, pen);
    }

    public void DrawVideoSource(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        if (source.Read(frame, out var bitmapRef))
        {
            using (bitmapRef)
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
        }
    }

    public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        ConfigureFillPaint(rect, fill);
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

    public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        ConfigureFillPaint(rect, fill);
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

    public void DrawText(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
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
            Canvas.DrawPath(skPath, _sharedFillPaint);
        }

        if (pen != null && pen.Thickness > 0)
        {
            ConfigureStrokePaint(rect, pen);

            using SKPath strokePath = PenHelper.CreateStrokePath(skPath, pen, rect);
            Canvas.DrawPath(strokePath, _sharedStrokePaint);
        }
    }

    public void DrawGeometry(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    {
        VerifyAccess();
        SKPath skPath = geometry.GetCachedPath();
        Rect rect = geometry.Bounds;

        ConfigureFillPaint(geometry.Bounds, fill);
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

    public void Pop(int count = -1)
    {
        VerifyAccess();

        if (count < 0)
        {
            while (count < 0
                   && _states.TryPop(out CanvasPushedState? state))
            {
                state.Pop(this);
                count++;
            }
        }
        else
        {
            while (_states.Count >= count
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
        int count;
        if (limit == default)
        {
            count = Canvas.SaveLayer();
        }
        else
        {
            using (var paint = new SKPaint())
            {
                count = Canvas.SaveLayer(limit.ToSKRect(), paint);
            }
        }

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    internal PushedState PushPaint(SKPaint paint, Rect? rect = null)
    {
        VerifyAccess();
        int count;
        if (rect.HasValue)
            count = Canvas.SaveLayer(rect.Value.ToSKRect(), paint);
        else
            count = Canvas.SaveLayer(paint);

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        int count = Canvas.Save();
        ClipRect(clip, operation);

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushClip(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        int count = Canvas.Save();
        ClipPath(geometry, operation);

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushOpacity(float opacity)
    {
        VerifyAccess();
        float oldOpacity = Opacity;
        Opacity *= opacity;
        var paint = new SKPaint();

        int count = Canvas.SaveLayer(paint);
        paint.Color = new SKColor(0, 0, 0, (byte)(Opacity * 255));
        _states.Push(new CanvasPushedState.OpacityPushedState(oldOpacity, count, paint));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false)
    {
        VerifyAccess();
        var paint = new SKPaint();

        int count = Canvas.SaveLayer(paint);
        new BrushConstructor(
            bounds, mask, (BlendMode)paint.BlendMode, RenderIntent, _currentDensity, MaxWorkingScale,
            pullPurpose: PullPurpose)
            .ConfigurePaint(paint);
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

    public PushedState PushBlendMode(BlendMode blendMode) => PushBlendMode(blendMode, colorFilter: null);

    // The color filter, when set, transforms the layer's pixels before the blend (the composite-fold path, C9);
    // the caller owns the filter's lifetime — disposing the paint on Pop does not free it. `layerBounds` limits
    // the SaveLayer to that rect (in the current canvas space); Skia widens the layer itself when the filter
    // affects transparent black, so a bounded layer never changes pixels — it only avoids the full-canvas
    // round trip. Callers must not bound the layer under a blend mode that alters the destination where the
    // source is transparent.
    internal PushedState PushBlendMode(BlendMode blendMode, SKColorFilter? colorFilter, Rect? layerBounds = null)
    {
        VerifyAccess();
        BlendMode tmp = BlendMode;
        BlendMode = blendMode;
        var paint = new SKPaint();
        paint.BlendMode = (SKBlendMode)blendMode;
        paint.ColorFilter = colorFilter;

        int count = layerBounds is { } bounds ? Canvas.SaveLayer(bounds.ToSKRect(), paint) : Canvas.SaveLayer(paint);
        _states.Push(new CanvasPushedState.BlendModePushedState(tmp, count, paint));
        return new PushedState(this, _states.Count);
    }

    internal void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        _dispatcher?.VerifyAccess();
    }

    private void ConfigureStrokePaint(Rect bounds, Pen.Resource? pen, BlendMode blendMode = BlendMode.SrcOver, float? scale = null)
    {
        _sharedStrokePaint.Reset();

        if (pen != null && pen.Thickness != 0)
        {
            _sharedStrokePaint.IsStroke = false;
            new BrushConstructor(
                bounds, pen.Brush, blendMode, RenderIntent, scale ?? _currentDensity, MaxWorkingScale,
                pullPurpose: PullPurpose)
                .ConfigurePaint(_sharedStrokePaint);
        }
    }

    private void ConfigureFillPaint(Rect bounds, Brush.Resource? brush, BlendMode blendMode = BlendMode.SrcOver, float? scale = null)
    {
        _sharedFillPaint.Reset();
        new BrushConstructor(
            bounds, brush, blendMode, RenderIntent, scale ?? _currentDensity, MaxWorkingScale,
            pullPurpose: PullPurpose)
            .ConfigurePaint(_sharedFillPaint);
    }
}
