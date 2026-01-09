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

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (!Source.HasValue) return [];

        return
        [
            RenderNodeOperation.CreateLambda(
                bounds: Bounds,
                render: canvas =>
                {
                    canvas.DrawImageSource(Source.Value.Resource, Fill?.Resource, Pen?.Resource);
                },
                hitTest: HitTest
            )
        ];
    }

    private bool HitTest(Point point)
    {
        StrokeAlignment alignment = Pen?.Resource.StrokeAlignment ?? StrokeAlignment.Inside;
        float thickness = Pen?.Resource.Thickness ?? 0;
        thickness = PenHelper.GetRealThickness(alignment, thickness);

        if (Fill != null)
        {
            Rect rect = Bounds.Inflate(thickness);
            return rect.ContainsExclusive(point);
        }
        else
        {
            Rect borderRect = Bounds.Inflate(thickness);
            Rect emptyRect = Bounds.Deflate(thickness);
            return borderRect.ContainsExclusive(point) && !emptyRect.ContainsExclusive(point);
        }
    }
}
