using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public sealed class ImageSourceRenderNode(ImageSource.Resource source, Brush.Resource? fill)
    : BrushRenderNode(fill, null)
{
    public (ImageSource.Resource Resource, int Version)? Source { get; private set; } = source.Capture();

    public Rect Bounds { get; private set; } = new(default, source.FrameSize.ToSize(1));

    public bool Update(ImageSource.Resource source, Brush.Resource? fill)
    {
        bool changed = Update(fill, null);
        if (!source.Compare(Source))
        {
            Source = source.Capture();
            changed = true;
        }

        if (changed && Source.HasValue)
        {
            Bounds = new Rect(default, Source.Value.Resource.FrameSize.ToSize(1));
        }

        if (changed)
        {
            MarkChanged();
        }

        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Source is not { } sourceSnapshot)
            return;

        Rect bounds = Bounds;
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        ImageSource.Resource source = sourceSnapshot.Resource;
        Brush.Resource? fill = Fill?.Resource;
        RenderResource<ImageSource.Resource> sourceResource = context.Borrow(source);

        context.Publish(context.PaintedSource(
            state: source,
            draw: static (canvas, fill, _, state) =>
                canvas.DrawImageSource(state, fill),
            fill: fill,
            pen: null,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.Custom(HitTest),
            scale: RenderScaleContract.Custom(static _ => 1f),
            directReplayAtExactIntegerReduction: true,
            resources: [sourceResource]));
    }

    private bool HitTest(RenderHitTestContext _, Point point)
    {
        if (Source is not { } source)
            return false;
        Rect fillBounds = new(default, source.Resource.FrameSize.ToSize(1));
        return Fill?.Resource is not null && fillBounds.ContainsExclusive(point);
    }
}
