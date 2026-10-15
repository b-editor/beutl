using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public sealed class VideoSourceRenderNode(
    VideoSource.Resource source,
    int frame,
    Brush.Resource? fill)
    : BrushRenderNode(fill, null)
{
    public (VideoSource.Resource Resource, int Version)? Source { get; private set; } = source.Capture();

    public int Frame { get; private set; } = frame;

    public Rect Bounds { get; private set; } = new(default, source.LogicalFrameSize.ToSize(1));

    public bool Update(VideoSource.Resource source, int frame, Brush.Resource? fill)
    {
        bool changed = Update(fill, null);
        if (!source.Compare(Source))
        {
            Source = source.Capture();
            changed = true;
        }

        if (changed && Source.HasValue)
        {
            Bounds = new Rect(default, Source.Value.Resource.LogicalFrameSize.ToSize(1));
        }

        if (Frame != frame)
        {
            Frame = frame;
            changed = true;
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

        int frame = Frame;
        VideoSource.Resource source = sourceSnapshot.Resource;
        Brush.Resource? fill = Fill?.Resource;
        float supplyDensity = source.SupplyDensity;
        RenderResource<VideoSource.Resource> sourceResource = context.Borrow(source);

        context.Publish(context.PaintedSource(
            state: (source, frame),
            draw: static (canvas, fill, _, state) =>
                canvas.DrawVideoSource(state.source, state.frame, fill),
            fill: fill,
            pen: null,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.Custom(HitTest),
            scale: RenderScaleContract.Custom(
                supplyDensity,
                static (density, _) => density),
            directReplayAtExactIntegerReduction: false,
            resources: [sourceResource]));
    }

    private bool HitTest(RenderHitTestContext _, Point point)
    {
        if (Source is not { } source)
            return false;
        Rect fillBounds = new(default, source.Resource.LogicalFrameSize.ToSize(1));
        return Fill?.Resource is not null && fillBounds.ContainsExclusive(point);
    }
}
