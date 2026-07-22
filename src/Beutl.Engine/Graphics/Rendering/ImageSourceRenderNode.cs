using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public sealed class ImageSourceRenderNode(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public (ImageSource.Resource Resource, int Version)? Source { get; private set; } = source.Capture();

    public Rect Bounds { get; private set; } = PenHelper.GetBounds(new Rect(default, source.FrameSize.ToSize(1)), pen);

    public bool Update(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        if (!source.Compare(Source))
        {
            Source = source.Capture();
            changed = true;
        }

        if (changed && Source.HasValue)
        {
            Bounds = PenHelper.GetBounds(new Rect(default, Source.Value.Resource.FrameSize.ToSize(1)), Pen?.Resource);
        }

        HasChanges = changed;
        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Source is not { } sourceSnapshot)
            return;

        Rect bounds = Bounds;
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        ImageSource.Resource source = sourceSnapshot.Resource;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        RenderResource<ImageSource.Resource> sourceResource = context.Borrow(
            source,
            source.GetOriginal().Id,
            sourceSnapshot.Version);
        RecordedPaint paint = BrushRecorder.RecordPaint(
            context,
            fill,
            fillSnapshot?.Version ?? 0,
            pen,
            penSnapshot?.Version ?? 0,
            bounds);
        var hitTestState = new ImageHitTestState(
            bounds,
            fill is not null,
            pen?.StrokeAlignment ?? StrokeAlignment.Inside,
            pen?.Thickness ?? 0);

        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            execute: session => DeferredOpaqueSource.Execute(
                session,
                sourceResource,
                paint,
                static (canvas, currentSource, currentFill, currentPen) =>
                    canvas.DrawImageSource(currentSource, currentFill, currentPen)),
            bounds: BrushRecorder.CreateSourceBounds(paint, bounds, typeof(ImageSourceRenderNode)),
            hitTest: RenderHitTestContract.Custom(
                hitTestState.Evaluate,
                typeof(ImageSourceRenderNode)),
            valueCardinality: RenderValueCardinality.Single,
            // Bitmap at native 1:1 density; downstream transforms re-scale accordingly.
            scale: RenderScaleContract.Custom(static _ => 1f, typeof(ImageSourceRenderNode)),
            structuralKey: typeof(ImageSourceRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity((bounds, hitTestState)),
            resources: DeferredOpaqueSource.Resources(
                [sourceResource, .. paint.Resources]));
        context.Publish(BrushRecorder.RecordSource(context, paint, description));
    }

    private readonly record struct ImageHitTestState(
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
}
