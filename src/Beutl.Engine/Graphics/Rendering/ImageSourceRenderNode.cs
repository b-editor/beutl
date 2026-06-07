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
                hitTest: HitTest,
                // feature 003 (FR-018): a decoded image is a bitmap at its native 1:1 density (its logical bounds
                // == its pixel size), so it reports a concrete supply density rather than Unbounded. This caps the
                // effect applied to it at the source's own resolution instead of upsampling it under export SSAA.
                // (The density is carried UNCHANGED through transforms — TransformRenderNode forwards but does not
                // scale it, since scaling would change s_out == 1 output.) At(1) is byte-identical to Unbounded at
                // s_out == 1 (both resolve w == 1).
                effectiveScale: EffectiveScale.At(1f)
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
