using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public sealed class VideoSourceRenderNode(
    IVideoSource source,
    int frame,
    Brush.Resource? fill,
    Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public IVideoSource Source { get; private set; } = source.Clone();

    public int Frame { get; private set; } = frame;

    public Rect Bounds { get; private set; } = PenHelper.GetBounds(new Rect(default, source.FrameSize.ToSize(1)), pen);

    public bool Update(IVideoSource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        if (!Source.Equals(source))
        {
            Source.Dispose();
            Source = source.Clone();
            changed = true;
        }

        if (changed)
        {
            Bounds = PenHelper.GetBounds(new Rect(default, Source.FrameSize.ToSize(1)), Pen?.Resource);
        }

        if (Frame != frame)
        {
            Frame = frame;
            changed = true;
        }

        HasChanges = changed;
        return changed;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(
                bounds: Bounds,
                render: canvas =>
                {
                    if (Source.Read(Frame, out IBitmap? bitmap))
                    {
                        using (bitmap)
                        {
                            canvas.DrawBitmap(bitmap, Fill?.Resource, Pen?.Resource);
                        }
                    }
                },
                hitTest: HitTest
            )
        ];
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Source.Dispose();
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
