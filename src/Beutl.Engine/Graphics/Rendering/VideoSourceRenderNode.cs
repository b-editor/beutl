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
            HasChanges = true;
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
        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        VideoSource.Resource source = sourceSnapshot.Resource;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        float supplyDensity = source.SupplyDensity;
        RenderResource<VideoSource.Resource> sourceResource = context.Borrow(source);
        var hitTestState = new VideoHitTestState(
            bounds,
            fill is not null,
            pen?.StrokeAlignment ?? StrokeAlignment.Inside,
            pen?.Thickness ?? 0);

        context.Publish(context.PaintedSource(
            state: (source, frame),
            draw: static (canvas, fill, pen, state) =>
                canvas.DrawVideoSource(state.source, state.frame, fill, pen),
            fill: fill,
            pen: pen,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.Custom(hitTestState.Evaluate),
            scale: RenderScaleContract.Custom(new VideoScaleResolver(supplyDensity).Resolve),
            resources: [sourceResource]));
    }

    private readonly record struct VideoHitTestState(
        Rect Bounds,
        bool HasFill,
        StrokeAlignment StrokeAlignment,
        float Thickness)
    {
        public bool HitTest(Point point)
        {
            float realThickness = PenHelper.GetRealThickness(StrokeAlignment, Thickness);

            if (HasFill)
            {
                Rect rect = Bounds.Inflate(realThickness);
                return rect.ContainsExclusive(point);
            }

            Rect borderRect = Bounds.Inflate(realThickness);
            Rect emptyRect = Bounds.Deflate(realThickness);
            return borderRect.ContainsExclusive(point) && !emptyRect.ContainsExclusive(point);
        }

        public bool Evaluate(RenderHitTestContext _, Point point) => HitTest(point);
    }

    private readonly record struct VideoScaleResolver(float SupplyDensity)
    {
        public float Resolve(RenderScaleContext _) => SupplyDensity;
    }
}
