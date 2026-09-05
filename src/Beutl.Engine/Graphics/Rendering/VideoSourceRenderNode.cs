using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public sealed class VideoSourceRenderNode(
    VideoSource.Resource source,
    int frame,
    Brush.Resource? fill,
    Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public (VideoSource.Resource Resource, int Version)? Source { get; private set; } = source.Capture();

    public int Frame { get; private set; } = frame;

    public Rect Bounds { get; private set; } = PenHelper.GetBounds(new Rect(default, source.LogicalFrameSize.ToSize(1)), pen);

    public bool Update(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        if (!source.Compare(Source))
        {
            Source = source.Capture();
            changed = true;
        }

        if (changed && Source.HasValue)
        {
            Bounds = PenHelper.GetBounds(new Rect(default, Source.Value.Resource.LogicalFrameSize.ToSize(1)), Pen?.Resource);
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
        Pen.Resource? pen = Pen?.Resource;
        float supplyDensity = source.SupplyDensity;
        RenderResource<VideoSource.Resource> sourceResource = context.Borrow(source);

        context.Publish(context.PaintedSource(
            state: (source, frame),
            draw: static (canvas, fill, pen, state) =>
                canvas.DrawVideoSource(state.source, state.frame, fill, pen),
            fill: fill,
            pen: pen,
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
        Rect bounds = Bounds;
        Pen.Resource? pen = Pen?.Resource;
        float realThickness = PenHelper.GetRealThickness(
            pen?.StrokeAlignment ?? StrokeAlignment.Inside,
            pen?.Thickness ?? 0);

        if (Fill?.Resource is not null)
        {
            return bounds.Inflate(realThickness).ContainsExclusive(point);
        }

        Rect borderRect = bounds.Inflate(realThickness);
        Rect emptyRect = bounds.Deflate(realThickness);
        return borderRect.ContainsExclusive(point) && !emptyRect.ContainsExclusive(point);
    }
}
