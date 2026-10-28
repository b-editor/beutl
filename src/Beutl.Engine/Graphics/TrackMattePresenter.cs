using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

// Evaluated inputs are borrowed from the scene compositor. Versioning includes animated resource
// changes, so both the render-node cache and every consumer of a shared matte see them.
internal sealed class TrackMatteContent
{
    private readonly List<Drawable.Resource> _drawables = [];
    private readonly List<int> _versions = [];

    public IReadOnlyList<Drawable.Resource> Drawables => _drawables;

    public int Version { get; private set; }

    public bool Update(ReadOnlySpan<EngineObject.Resource> inputs)
    {
        bool changed = false;
        int count = 0;
        foreach (EngineObject.Resource input in inputs)
        {
            if (input is not Drawable.Resource drawable) continue;
            if (count == _drawables.Count)
            {
                _drawables.Add(drawable);
                _versions.Add(drawable.Version);
                changed = true;
            }
            else if (_drawables[count] != drawable || _versions[count] != drawable.Version)
            {
                _drawables[count] = drawable;
                _versions[count] = drawable.Version;
                changed = true;
            }

            count++;
        }

        if (count < _drawables.Count)
        {
            _drawables.RemoveRange(count, _drawables.Count - count);
            _versions.RemoveRange(count, _versions.Count - count);
            changed = true;
        }

        if (changed) Version++;
        return changed;
    }
}

// Like a clip transition presenter, this is compositor-owned and never saved into the document.
internal sealed partial class TrackMattePresenter : Drawable
{
    public TrackMattePresenter()
    {
        ScanProperties<TrackMattePresenter>();
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Matte is not { } matte) return;

        var domain = new Rect(context.Size);
        using (context.PushNode((r.Mode, domain),
                   static p => new TrackMatteRenderNode(p.Mode, p.domain),
                   static (node, p) => node.Update(p.Mode, p.domain)))
        {
            using (context.PushNode(domain,
                       static bounds => new TrackMatteLayerRenderNode(bounds),
                       static (node, bounds) => node.Update(bounds)))
            {
                foreach (Drawable.Resource drawable in r.Content.Drawables)
                    context.DrawDrawable(drawable);
            }

            context.DrawNode((matte, context.Size, context.OutputScale),
                static p => new TrackMatteSourceRenderNode(p.matte, p.Size, p.OutputScale),
                static (node, p) => node.Update(p.matte, p.Size, p.OutputScale));
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public new partial class Resource
    {
        private int _matteVersion = -1;

        public TrackMatteContent Content { get; } = new();

        public TrackMatteContent? Matte { get; private set; }

        public MatteMode Mode { get; private set; }

        internal void SetInputs(ReadOnlySpan<EngineObject.Resource> content, TrackMatteContent matte, MatteMode mode)
        {
            bool changed = Content.Update(content);
            if (Matte != matte || _matteVersion != matte.Version || Mode != mode)
            {
                Matte = matte;
                _matteVersion = matte.Version;
                Mode = mode;
                changed = true;
            }

            if (changed) Version++;
        }

        partial void PostDispose(bool disposing)
        {
            Content.Update([]);
            Matte = null;
        }
    }
}
