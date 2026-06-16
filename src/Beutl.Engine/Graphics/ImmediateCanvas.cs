using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas : ICanvas
{
    internal readonly RenderTarget _renderTarget;
    private readonly Dispatcher? _dispatcher;
    private readonly SKPaint _sharedFillPaint = new();
    private readonly SKPaint _sharedStrokePaint = new();
    private readonly Stack<CanvasPushedState> _states = new();
    private Matrix _currentTransform;
    // feature 003: base CTM CreateScale(SurfaceDensity), baked at construction (identity when density == 1),
    // so logical geometry maps onto the ceil(logical × density) device buffer automatically.
    private readonly Matrix _baseTransform;
    // SKCanvas save depth that pins the base CTM below every Push/Pop; -1 when density == 1 (no base Save, so
    // the matrix / save stack is never touched — the byte-identity anchor). Default -1 keeps Dispose's
    // finalizer path a no-op if the constructor throws before the base Save (must not deref a half-built Canvas).
    private readonly int _baseSaveCount = -1;
    // Density of the CURRENT coordinate space (push/pop): SurfaceDensity normally, 1 inside a PushDeviceSpace()
    // block. Brush fills and nested pulls read it to match the active CTM.
    private float _currentDensity;
    // Base matrix the Set transform operator re-applies (push/pop): _baseTransform normally, identity inside
    // PushDeviceSpace() so an absolute-device Set does not re-inject the surface density.
    private Matrix _currentBaseTransform;

    public ImmediateCanvas(RenderTarget renderTarget, float density = 1f,
        float maxWorkingScale = float.PositiveInfinity, Size logicalSize = default)
    {
        if (density <= 0f || !float.IsFinite(density))
            throw new ArgumentOutOfRangeException(nameof(density), density,
                "Density must be a positive finite value.");

        _dispatcher = Dispatcher.Current;
        _renderTarget = renderTarget;
        Canvas = _renderTarget.Value.Canvas;
        DeviceSize = new PixelSize(renderTarget.Width, renderTarget.Height);
        // The declared logical viewport. When omitted, assume the buffer is exactly logical × density, i.e.
        // logical = device ÷ density.
        LogicalSize = logicalSize.IsDefault ? DeviceSize.ToSize(density) : logicalSize;
        SurfaceDensity = density;
        _currentDensity = density;
        MaxWorkingScale = maxWorkingScale;
        if (density == 1f)
        {
            // TRUE no-op — byte-identity anchor. Never touch the matrix or the save stack.
            _baseTransform = Matrix.Identity;
            _baseSaveCount = -1;
            _currentTransform = Canvas.TotalMatrix.ToMatrix();
        }
        else
        {
            // Pin the base scale below all Push/Pop: SKCanvasPushedState.Pop re-syncs _currentTransform from
            // TotalMatrix and RestoreToCount cannot unwind past this Save, so every pop lands on the base.
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

    /// <summary>
    /// The declared logical viewport (feature 003), independent of the device pixel size; the base CTM maps it
    /// onto the <see cref="DeviceSize"/> buffer. Used as the layout / stretch viewport for a nested
    /// <see cref="DrawDrawable"/> build context.
    /// </summary>
    public Size LogicalSize { get; }

    /// <summary>The physical backing-surface size in device pixels (<c>ceil(LogicalSize × SurfaceDensity)</c>).</summary>
    public PixelSize DeviceSize { get; }

    /// <summary>
    /// Pixel density of the CURRENT coordinate space (feature 003): device px per unit of the space the next
    /// draw uses. Equals <see cref="SurfaceDensity"/> normally; 1 inside a <see cref="PushDeviceSpace"/> block.
    /// Brush fills and nested pulls read this to match the active CTM (a device-space block must not re-densify
    /// brush content).
    /// </summary>
    public float Density => _currentDensity;

    /// <summary>
    /// The immutable density the backing surface is rasterized at (feature 003): device px per logical unit,
    /// fixed at construction. The base CTM is <c>CreateScale(SurfaceDensity)</c>, and a <see cref="Snapshot"/>
    /// captures the whole surface at this density — not <see cref="Density"/>, which a device-space block
    /// lowers to 1, mis-tagging the capture. On the root canvas this is the request's output scale
    /// <c>s_out</c>; on a nested buffer it is that buffer's working density <c>w</c>.
    /// </summary>
    public float SurfaceDensity { get; }

    /// <summary>
    /// The render request's working-scale ceiling (feature 003, FR-037), forwarded into nested pulls started
    /// from this canvas (drawable-brush children, <see cref="DrawDrawable"/>/<see cref="DrawNode"/>) so a
    /// high-density source inside them cannot escape it. <c>+∞</c> (default) = no ceiling.
    /// </summary>
    public float MaxWorkingScale { get; }

    public Matrix Transform
    {
        get { return _currentTransform; }
        // feature 003: a raw SetMatrix that bypasses the base CTM; internal so out-of-tree code cannot clobber
        // the pinned base (public mutation goes through PushTransform). A read-then-set such as
        // `Transform = m * Transform` still preserves the base because the getter includes it.
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
            // Mark disposed + suppress the finalizer first: if a GPU op below throws (context-lost or
            // half-built canvas), it must not skip SuppressFinalize and let the finalizer re-run DisposeCore
            // and crash the process on the finalizer thread.
            IsDisposed = true;
            GC.SuppressFinalize(this);
            try
            {
                // Flush first: submit this canvas's recorded (deferred) GPU work while its surface and paints
                // are still alive. This is a GRContext-level flush of the shared context — a superset that also
                // submits every other surface's pending work, but necessarily includes this canvas's, which is
                // what matters. Resetting the save stack or disposing the paints before the flush would let Skia
                // reference freed GPU resources at flush time (a leaked SKObject then throws in its finalizer
                // under software Vulkan, aborting the process).
                GraphicsContextFactory.SharedContext?.SkiaContext.Flush(true, true);

                // feature 003 (CI host crash): undo the base Save() taken at construction (density != 1) so a
                // reused SKCanvas does not inherit this canvas's save depth or base matrix. density == 1 took no
                // base Save (_baseSaveCount < 0), so it restores nothing — the byte-identity anchor is preserved.
                //
                // Guard Canvas.Handle: the SKCanvas is a child wrapper of the SKSurface (owns: false), cached at
                // construction. SkiaSharp zeroes a wrapper's Handle when it disposes it, and disposes this child
                // wrapper when the GrContext/surface is torn down — which can happen while the RenderTarget is
                // still ref-alive (the SKSurface and its ref count survive, the wrapper does not), e.g. shared-
                // GrContext teardown during GC. RestoreToCount on a zero-Handle wrapper passes a null SkCanvas*
                // into native Skia and SIGSEGVs the render thread — an uncatchable fault the try/catch and a
                // non-null managed-wrapper check both miss. If the wrapper is gone there is nothing to restore.
                if (_baseSaveCount >= 0 && Canvas is not null && Canvas.Handle != IntPtr.Zero)
                {
                    Canvas.RestoreToCount(_baseSaveCount);
                }
            }
            catch
            {
                // Best-effort GPU-state cleanup; never abort disposal (or crash the finalizer thread) on it.
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

        surface.Flush(true, true);
    }

    public void DrawRenderTarget(RenderTarget renderTarget, Point point)
    {
        // NOTE: renderTargetを保持しておいて次回Flushされたときに開放すると効率的
        renderTarget.VerifyAccess();
        _sharedFillPaint.Reset();
        _sharedFillPaint.IsAntialias = true;

        Canvas.DrawSurface(renderTarget.Value, point.X, point.Y, _sharedFillPaint);

        renderTarget.Value.Flush(true, true);
    }

    // feature 003 (FR-017/T034): draw a concrete-scale buffer into a LOGICAL destination rect, so the active
    // CTM maps it to the device surface. Mitchell resample handles working-scale != output-scale; when the
    // buffer pixel size already equals the destination's device size the caller must use the bare point-based
    // blit instead (the equal-scale short-circuit is the caller's responsibility).
    // Distinct name (not an overload) to avoid ambiguity with the Point overload at `default` call sites.
    public void DrawRenderTargetScaled(RenderTarget renderTarget, Rect dest)
    {
        VerifyAccess();
        renderTarget.VerifyAccess();

        using SKImage image = renderTarget.Value.Snapshot();
        DrawImageScaled(image, dest);

        renderTarget.Value.Flush(true, true);
    }

    // feature 003: draw a PRE-SNAPSHOTTED image into a LOGICAL destination rect (Mitchell). A caller blitting
    // the same buffer many times (the particle hot path) snapshots once and reuses the SKImage here instead of
    // re-snapshotting + force-flushing the source per draw — avoiding thousands of per-frame SKImage
    // allocations and synchronous GPU flushes on the non-unit-scale render path.
    public void DrawImageScaled(SKImage image, Rect dest)
    {
        VerifyAccess();
        _sharedFillPaint.Reset();
        _sharedFillPaint.IsAntialias = true;

        var src = SKRect.Create(image.Width, image.Height);
        Canvas.DrawImage(image, src, dest.ToSKRect(), new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);
    }

    // feature 003: SKSurface counterpart of DrawRenderTargetScaled — draw a concrete-scale surface into its
    // own LOGICAL footprint so the active CTM maps it to the device surface (used for nested-scene / 3D bitmap
    // ops whose backing surface is denser than the destination). The destination is derived from the surface's
    // OWN pixel size ÷ density (origin-anchored), NOT a caller-supplied bounds — mirroring
    // DrawRenderTargetScaled/CreateFromRenderTarget so a downstream filter that inflated the op bounds while the
    // buffer still holds the original area cannot stretch it.
    public void DrawSurfaceScaled(SKSurface surface, Point origin, float scale)
    {
        VerifyAccess();
        _sharedFillPaint.Reset();
        _sharedFillPaint.IsAntialias = true;

        using SKImage image = surface.Snapshot();
        var src = SKRect.Create(image.Width, image.Height);
        var dest = SKRect.Create((float)origin.X, (float)origin.Y, image.Width / scale, image.Height / scale);
        Canvas.DrawImage(image, src, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), _sharedFillPaint);

        surface.Flush(true, true);
    }

    public void DrawDrawable(Drawable.Resource drawable)
    {
        using var node = new DrawableRenderNode(drawable);
        // feature 003: the nested build context gets the LOGICAL viewport (not the device buffer size — that
        // fed device px where a logical size is expected) so layout / stretch stays scale-independent, plus the
        // current density so its sub-pulls rasterize to match this canvas.
        using var context = new GraphicsContext2D(node, LogicalSize, _currentDensity);
        drawable.GetOriginal().Render(context, drawable);
        // Forward this canvas's current density and ceiling so the nested pull rasterizes at the surface
        // density and cannot escape the request's FR-037 ceiling.
        var processor = new RenderNodeProcessor(node, true, _currentDensity, MaxWorkingScale);
        processor.Render(this);
    }

    public void DrawNode(RenderNode node)
    {
        var processor = new RenderNodeProcessor(node, true, _currentDensity, MaxWorkingScale);
        processor.Render(this);
    }

    public void DrawBackdrop(IBackdrop backdrop)
    {
        backdrop.Draw(this);
    }

    public IBackdrop Snapshot()
    {
        // feature 003 (CSM-3/CSM3-1): record the density this surface was captured at so the backdrop un-scales
        // by it on replay. Use SurfaceDensity, not the current Density (a device-space block lowers that to 1):
        // it is the whole surface's density — s_out on the root canvas, working density w on a nested flush canvas.
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

    // feature 003: draw a bitmap into a LOGICAL destination rect (not at pixel-extent), so a device-resolution
    // capture is mapped back to its logical footprint by the active CTM instead of being double-scaled.
    // Used by the snapshot-backdrop path at s_out != 1.
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
                DrawBitmap(bitmapRef.Value, fill, pen);
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
        SKTextBlob textBlob = text.GetTextBlob();

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
        new BrushConstructor(bounds, mask, (BlendMode)paint.BlendMode, _currentDensity, MaxWorkingScale).ConfigurePaint(paint);
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
            // feature 003: Set re-applies the CURRENT base CTM (surface density, or identity inside a
            // PushDeviceSpace block) instead of clobbering it to a bare matrix, so an absolute Set keeps the
            // canvas in the right coordinate space. At density 1 the base is identity (byte-identical).
            Transform = _currentBaseTransform.Prepend(matrix);
        }

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    /// <summary>
    /// feature 003: enter ABSOLUTE device space for the lifetime of the returned state — the CTM becomes
    /// identity (1 unit = 1 device px), <see cref="Density"/> drops to 1, and the Set base becomes identity,
    /// all restored on dispose (so nesting returns to the enclosing state, not the base). Use it to draw
    /// device-px content (a contour traced from the device buffer, a point-blit of another device buffer, a
    /// full-buffer shader rect) onto a density-aware canvas. At <see cref="SurfaceDensity"/> == 1 with no
    /// ambient transform the CTM is already identity, so this is a no-op-shaped block.
    /// </summary>
    public PushedState PushDeviceSpace()
    {
        VerifyAccess();

        // True no-op when the current space is ALREADY absolute device space (density 1, identity CTM and Set-
        // base): entering would only emit a redundant Save + SetMatrix(identity). Skipping it keeps the
        // density-1 path's SKCanvas command stream byte-identical to the pre-feature path (so a device-effect
        // like InnerShadow draws the exact same blur SaveLayer it did before this feature).
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
        BlendMode tmp = BlendMode;
        BlendMode = blendMode;
        var paint = new SKPaint();
        paint.BlendMode = (SKBlendMode)blendMode;

        int count = Canvas.SaveLayer(paint);
        _states.Push(new CanvasPushedState.BlendModePushedState(tmp, count, paint));
        return new PushedState(this, _states.Count);
    }

    internal void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        _dispatcher?.VerifyAccess();
    }

    private void ConfigureStrokePaint(Rect bounds, Pen.Resource? pen, BlendMode blendMode = BlendMode.SrcOver)
    {
        _sharedStrokePaint.Reset();

        if (pen != null && pen.Thickness != 0)
        {
            _sharedStrokePaint.IsStroke = false;
            new BrushConstructor(bounds, pen.Brush, blendMode, _currentDensity, MaxWorkingScale).ConfigurePaint(_sharedStrokePaint);
        }
    }

    private void ConfigureFillPaint(Rect bounds, Brush.Resource? brush, BlendMode blendMode = BlendMode.SrcOver)
    {
        _sharedFillPaint.Reset();
        new BrushConstructor(bounds, brush, blendMode, _currentDensity, MaxWorkingScale).ConfigurePaint(_sharedFillPaint);
    }
}
