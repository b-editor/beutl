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

    public Rect Bounds { get; private set; } = PenHelper.GetBounds(new Rect(default, source.FrameSize.ToSize(1)), pen);

    public bool Update(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        if (source.Compare(Source))
        {
            Source = source.Capture();
            changed = true;
        }

        if (changed && Source.HasValue)
        {
            Bounds = PenHelper.GetBounds(new Rect(default, Source.Value.Resource.FrameSize.ToSize(1)), Pen?.Resource);
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
        if (!Source.HasValue) return [];

        return
        [
            RenderNodeOperation.CreateLambda(
                bounds: Bounds,
                render: canvas =>
                {
                    if (Source.Value.Resource.Read(Frame, out IBitmap? bitmap))
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
