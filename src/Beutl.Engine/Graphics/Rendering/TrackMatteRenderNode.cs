using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal sealed class TrackMatteRenderNode(MatteMode mode, Rect domain) : ContainerRenderNode
{
    private static readonly RenderHitTestContract s_hitTest = RenderHitTestContract.Custom(
        static (context, point) => context.OutputBounds.Contains(point) && context.Inputs[0].HitTest(point));

    public bool Update(MatteMode newMode, Rect newDomain)
    {
        if (mode == newMode && domain == newDomain) return false;
        mode = newMode;
        domain = newDomain;
        MarkChanged();
        return true;
    }

    public override void Process(RenderNodeContext context)
    {
        RenderFragmentHandle content = context.Inputs[0];
        RenderFragmentHandle matte = context.Inputs[1];
        if (mode is MatteMode.Luma or MatteMode.InvertedLuma)
            matte = context.Shader(matte, BuiltInColorFilterShader.LumaColor());

        BlendMode blend = mode is MatteMode.InvertedAlpha or MatteMode.InvertedLuma
            ? BlendMode.DstOut : BlendMode.DstIn;
        // The finite layer includes transparent pixels outside the source's geometry. DstIn must
        // clear those pixels too, without touching any of the scene's layers behind this element.
        context.Publish(context.Layer([content, context.Blend(matte, blend)], domain, s_hitTest));
    }
}

internal sealed class TrackMatteLayerRenderNode(Rect domain) : ContainerRenderNode
{
    public bool Update(Rect newDomain)
    {
        if (domain == newDomain) return false;
        domain = newDomain;
        MarkChanged();
        return true;
    }

    public override void Process(RenderNodeContext context)
        => context.Publish(context.Layer(context.Inputs, domain));
}

// Each consumer owns its recording tree. Its isolated output is recorded once per render request,
// keyed by the compositor's shared content, so multiple consumers reuse the same GPU value.
internal sealed class TrackMatteSourceRenderNode : RenderNode
{
    private readonly TrackMatteLayerRenderNode _root;
    private readonly RenderNode[] _children;
    private TrackMatteContent? _content;
    private int _version = -1;
    private Size _size;
    private float _scale;

    public TrackMatteSourceRenderNode(TrackMatteContent content, Size size, float scale)
    {
        _root = new TrackMatteLayerRenderNode(new Rect(size));
        _children = [_root];
        try
        {
            Update(content, size, scale);
        }
        catch
        {
            _root.Dispose();
            throw;
        }
    }

    public override ReadOnlySpan<RenderNode> ChildNodes => _children;

    public bool Update(TrackMatteContent content, Size size, float scale)
    {
        if (_content == content && _version == content.Version && _size == size && _scale == scale)
            return false;

        _root.Update(new Rect(size));
        using (var graphics = new GraphicsContext2D(_root, size, scale))
        {
            foreach (Drawable.Resource drawable in content.Drawables)
                graphics.DrawDrawable(drawable);
        }

        _content = content;
        _version = content.Version;
        _size = size;
        _scale = scale;
        MarkChanged();
        return true;
    }

    public override void Process(RenderNodeContext context)
        => context.PublishRange(context.RecordSharedSubtree(_content!, _root));

    protected override void OnDispose(bool disposing)
    {
        _root.Dispose();
        _content = null;
    }
}
