using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics.Rendering;

public sealed class VideoSourceNode(
    IVideoSource source, int frame, IBrush? fill, IPen? pen)
    : BrushDrawNode(fill, pen, PenHelper.GetBounds(new Rect(default, source.FrameSize.ToSize(1)), pen))
{
    public IVideoSource Source { get; } = source.Clone();

    public int Frame { get; } = frame;

    public bool Equals(IVideoSource source, int frame, IBrush? fill, IPen? pen)
    {
        return Frame == frame
            && EqualityComparer<IVideoSource?>.Default.Equals(Source, source)
            && EqualityComparer<IBrush?>.Default.Equals(Fill, fill)
            && EqualityComparer<IPen?>.Default.Equals(Pen, pen);
    }

    public override void Render(ImmediateCanvas canvas)
    {
        if (Source.Read(Frame, out IBitmap? bitmap))
        {
            using (bitmap)
            {
                canvas.DrawBitmap(bitmap, Fill, Pen);
            }
        }
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Source.Dispose();
    }

    public override bool HitTest(Point point)
    {
        StrokeAlignment alignment = Pen?.StrokeAlignment ?? StrokeAlignment.Inside;
        float thickness = Pen?.Thickness ?? 0;
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
