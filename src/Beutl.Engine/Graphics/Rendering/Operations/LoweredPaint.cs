using Beutl.Media;

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
    /// <para>
    /// The only addressing mode a painted source has, and the reason this session carries no token-taking
    /// <c>UseResource</c> the way the other session types do. <see cref="RenderNodeContext.PaintedSource"/> is
    /// state-passing only: the state is the produced value's output-cache runtime identity, a
    /// <see cref="RenderResource{T}"/> in a tuple element is rejected, and so is a capturing callback. A sealed
    /// non-tuple state does pass validation and physically delivers a token, but it is an enumerated identity
    /// channel rather than a way to address resources: the author then owns the identity contract by hand. A
    /// holder allocated per recording loses output-cache reuse; a reused or value-equal holder keeps reuse but
    /// its identity no longer tracks the resource, so a pixel-affecting change can be served from a stale cached
    /// output — a node that keeps one holder and mutates it in place draws its first frame once and then serves
    /// those pixels for every later frame; and a token left over from a finished request throws when leased.
    /// Position is the address by design, not by impossibility.
    /// </para>
    /// <para>
    /// The index addresses the <c>resources</c> argument the author passed to
    /// <see cref="RenderNodeContext.PaintedSource"/> and nothing else. The brush and pen slots the recorder
    /// added for the lowered paint live in a separate engine-owned space, so adding or removing a drawable
    /// brush never shifts an author's index. <typeparamref name="T"/> is the only check on the index: two
    /// declared resources of the same type make index 0 and index 1 indistinguishable.
    /// </para>
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
