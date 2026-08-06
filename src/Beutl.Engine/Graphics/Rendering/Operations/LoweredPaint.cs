using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// A brush bound to one running execution, carrying any nested <see cref="DrawableBrush"/> content that was
/// lowered while the request was recorded.
/// </summary>
/// <remarks>
/// The lowered content is a renderer-owned view leased for the duration of the draw callback. Nothing about it
/// is readable here, so an author can neither retain it past the callback nor release it.
/// </remarks>
public readonly struct LoweredBrush
{
    internal LoweredBrush(Brush.Resource? resource, BrushTileContent? tileContent)
    {
        Resource = resource;
        TileContent = tileContent;
    }

    /// <summary>Gets the brush that paints nothing.</summary>
    public static LoweredBrush Empty => default;

    /// <summary>Gets whether this brush paints nothing.</summary>
    public bool IsEmpty => Resource is null;

    internal Brush.Resource? Resource { get; }

    internal BrushTileContent? TileContent { get; }
}

/// <summary>A pen bound to one running execution, together with its <see cref="LoweredBrush"/>.</summary>
public readonly struct LoweredPen
{
    internal LoweredPen(Pen.Resource? resource, LoweredBrush brush)
    {
        Resource = resource;
        Brush = brush;
    }

    /// <summary>Gets the pen that strokes nothing.</summary>
    public static LoweredPen Empty => default;

    /// <summary>Gets whether this pen strokes nothing.</summary>
    public bool IsEmpty => Resource is null;

    internal Pen.Resource? Resource { get; }

    internal LoweredBrush Brush { get; }
}

/// <summary>The engine-owned facade a painted source's draw callback receives.</summary>
/// <remarks>
/// Its members are exactly those that mean the same thing whether the source materializes into its own target
/// or replays straight onto an existing one, so one authored callback serves both paths. It is a value so that
/// resolving a paint costs nothing on the render hot path; retaining a copy still reaches an inactive token.
/// </remarks>
public readonly struct PaintedRenderSession
{
    private readonly RenderExecutionSessionToken? _token;
    private readonly IReadOnlyList<RenderResource>? _resources;
    private readonly ImmediateCanvas? _canvas;
    private readonly LoweredBrush _fill;
    private readonly LoweredPen _pen;

    internal PaintedRenderSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        IReadOnlyList<RenderResource> resources,
        LoweredBrush fill,
        LoweredPen pen)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        _token = token;
        _canvas = canvas;
        _resources = resources;
        _fill = fill;
        _pen = pen;
    }

    /// <summary>Gets the canvas this source draws on.</summary>
    public ImmediateCanvas Canvas
    {
        get { Verify(); return _canvas!; }
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

    /// <summary>Uses a resource by its position in the source's own declared resource list.</summary>
    /// <remarks>
    /// The index addresses the <c>resources</c> argument the author passed to
    /// <see cref="RenderNodeContext.PaintedSource"/> and nothing else. The brush and pen slots the recorder
    /// added for the lowered paint live in a separate engine-owned space, so adding or removing a drawable
    /// brush never shifts an author's index.
    /// </remarks>
    public void UseDeclaredResource<T>(int declaredIndex, Action<T> use)
        where T : class
    {
        Verify();
        _token!.UseDeclaredResource(declaredIndex, _resources!, use);
    }

    private void Verify()
    {
        if (_token is null)
            throw new InvalidOperationException("default(PaintedRenderSession) is not a running execution.");

        _token.ThrowIfInactive();
    }
}
