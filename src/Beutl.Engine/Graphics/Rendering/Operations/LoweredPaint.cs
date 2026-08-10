using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// A brush bound to one running execution, carrying any nested <see cref="DrawableBrush"/> content that was
/// lowered while the request was recorded.
/// </summary>
/// <remarks>
/// The lowered content is a renderer-owned view leased for the duration of the draw callback, and nothing
/// about it is readable here. A copy that outlives the lease keeps no claim on it: every draw overload that
/// accepts a lowered paint asks the leasing execution whether the payload is still held, and rejects the copy
/// once it is not, whichever canvas the copy is handed to.
/// </remarks>
public readonly struct LoweredBrush
{
    private readonly RenderExecutionSessionToken? _lease;

    internal LoweredBrush(
        RenderExecutionSessionToken? lease,
        Brush.Resource? resource,
        BrushTileContent? tileContent)
    {
        _lease = lease;
        Resource = resource;
        TileContent = tileContent;
    }

    /// <summary>Gets the brush that paints nothing.</summary>
    public static LoweredBrush Empty => default;

    /// <summary>Gets whether this brush paints nothing.</summary>
    public bool IsEmpty => Resource is null;

    internal Brush.Resource? Resource { get; }

    internal BrushTileContent? TileContent { get; }

    /// <remarks>
    /// A brush with no lease was never resolved from a running execution — the empty brush, and the
    /// engine-internal resolution of an already-registered filter-effect brush — so it holds nothing that can
    /// expire and is always usable.
    /// </remarks>
    internal bool IsLeaseActive
        => _lease is not { } lease
           || (lease.IsResourceAuthorized(Resource!)
               && (TileContent is null || lease.IsResourceAuthorized(TileContent.Shader)));
}

/// <summary>A pen bound to one running execution, together with its <see cref="LoweredBrush"/>.</summary>
/// <remarks>Its lease behaves exactly like <see cref="LoweredBrush"/>'s.</remarks>
public readonly struct LoweredPen
{
    private readonly RenderExecutionSessionToken? _lease;

    internal LoweredPen(RenderExecutionSessionToken? lease, Pen.Resource? resource, LoweredBrush brush)
    {
        _lease = lease;
        Resource = resource;
        Brush = brush;
    }

    /// <summary>Gets the pen that strokes nothing.</summary>
    public static LoweredPen Empty => default;

    /// <summary>Gets whether this pen strokes nothing.</summary>
    public bool IsEmpty => Resource is null;

    internal Pen.Resource? Resource { get; }

    internal LoweredBrush Brush { get; }

    internal bool IsLeaseActive
        => (_lease is not { } lease || lease.IsResourceAuthorized(Resource!))
           && Brush.IsLeaseActive;
}

/// <summary>
/// The lease-bound, draw-only canvas exposed to a painted source callback.
/// </summary>
/// <remarks>
/// Every operation is observationally equivalent when the source draws into its own transparent target or is
/// replayed directly onto its consumer. Target-wide clears, raw paint resources, state mutation, native target
/// access, readback, nested rendering, and synchronization are intentionally absent.
/// </remarks>
public readonly struct PaintedRenderCanvas
{
    private readonly RenderExecutionSessionToken? _token;
    private readonly ImmediateCanvas? _canvas;

    internal PaintedRenderCanvas(RenderExecutionSessionToken token, ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(canvas);
        _token = token;
        _canvas = canvas;
    }

    /// <summary>Gets the active logical-to-device density.</summary>
    public float Density
    {
        get { Verify(); return _canvas!.Density; }
    }

    /// <summary>Draws a bitmap at its intrinsic logical size with the resolved fill and pen.</summary>
    public void DrawBitmap(Bitmap bitmap, LoweredBrush fill, LoweredPen pen)
    {
        Verify();
        _canvas!.DrawBitmap(bitmap, fill, pen);
    }

    /// <summary>Draws a bitmap scaled into the specified logical destination.</summary>
    public void DrawBitmapScaled(Bitmap bitmap, Rect destination, LoweredBrush fill)
    {
        Verify();
        _canvas!.DrawBitmapScaled(bitmap, destination, fill);
    }

    /// <summary>Draws a recorded image source with the resolved fill and pen.</summary>
    public void DrawImageSource(ImageSource.Resource source, LoweredBrush fill, LoweredPen pen)
    {
        Verify();
        _canvas!.DrawImageSource(source, fill, pen);
    }

    /// <summary>Draws one frame from a recorded video source with the resolved fill and pen.</summary>
    public void DrawVideoSource(VideoSource.Resource source, int frame, LoweredBrush fill, LoweredPen pen)
    {
        Verify();
        _canvas!.DrawVideoSource(source, frame, fill, pen);
    }

    /// <summary>Draws an ellipse inside the specified logical rectangle.</summary>
    public void DrawEllipse(Rect rect, LoweredBrush fill, LoweredPen pen)
    {
        Verify();
        _canvas!.DrawEllipse(rect, fill, pen);
    }

    /// <summary>Draws the specified logical rectangle.</summary>
    public void DrawRectangle(Rect rect, LoweredBrush fill, LoweredPen pen)
    {
        Verify();
        _canvas!.DrawRectangle(rect, fill, pen);
    }

    /// <summary>Draws recorded formatted text with the resolved fill and pen.</summary>
    public void DrawText(FormattedText text, LoweredBrush fill, LoweredPen pen)
    {
        Verify();
        _canvas!.DrawText(text, fill, pen);
    }

    /// <summary>Draws a recorded geometry with the resolved fill and pen.</summary>
    public void DrawGeometry(Geometry.Resource geometry, LoweredBrush fill, LoweredPen pen)
    {
        Verify();
        _canvas!.DrawGeometry(geometry, fill, pen);
    }

    private void Verify()
    {
        if (_token is null)
            throw new InvalidOperationException("default(PaintedRenderCanvas) is not a running execution.");

        _token.ThrowIfInactive();
    }
}

/// <summary>The engine-owned facade a painted source's draw callback receives.</summary>
/// <remarks>
/// Its members are exactly those that mean the same thing whether the source materializes into its own target
/// or replays straight onto an existing one, so one authored callback serves both paths. It is a value, so
/// receiving it and reading its paint allocate nothing; reaching a declared resource goes through a callback,
/// and a callback that reads this session or the source's state allocates one closure per draw.
/// A retained copy reaches an inactive token.
/// </remarks>
public readonly struct PaintedRenderSession
{
    private readonly RenderExecutionSessionToken? _token;
    private readonly IReadOnlyList<RenderResourceBinding>? _resourceBindings;
    private readonly PaintedRenderCanvas _canvas;
    private readonly LoweredBrush _fill;
    private readonly LoweredPen _pen;

    internal PaintedRenderSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        IReadOnlyList<RenderResourceBinding> resources,
        LoweredBrush fill,
        LoweredPen pen)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        _token = token;
        _canvas = new PaintedRenderCanvas(token, canvas);
        _resourceBindings = resources;
        _fill = fill;
        _pen = pen;
    }

    /// <summary>Gets the canvas this source draws on.</summary>
    public PaintedRenderCanvas Canvas
    {
        get { Verify(); return _canvas; }
    }

    /// <summary>Gets the recorded fill brush, resolved for this execution.</summary>
    public LoweredBrush Fill
    {
        get { Verify(); return _fill; }
    }

    /// <summary>Gets the recorded pen, resolved for this execution.</summary>
    public LoweredPen Pen
    {
        get { Verify(); return _pen; }
    }

    /// <summary>Uses an author-declared resource by its stable name.</summary>
    /// <remarks>Recorder-owned primary and paint resources use a separate engine-owned namespace.</remarks>
    public void UseDeclaredResource<T>(string name, Action<T> use)
        where T : class
    {
        Verify();
        _token!.UseDeclaredResource(name, _resourceBindings!, use);
    }

    private void Verify()
    {
        if (_token is null)
            throw new InvalidOperationException("default(PaintedRenderSession) is not a running execution.");

        _token.ThrowIfInactive();
    }
}
