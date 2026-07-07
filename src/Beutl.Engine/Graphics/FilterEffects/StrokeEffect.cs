using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
public partial class StrokeEffect : FilterEffect
{
    public enum StrokeStyles
    {
        Background,
        Foreground,
    }

    public StrokeEffect()
    {
        ScanProperties<StrokeEffect>();
        Pen.CurrentValue = new Pen();
    }

    [Display(Name = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    [Display(Name = nameof(GraphicsStrings.Offset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    [Display(Name = nameof(GraphicsStrings.StrokeEffect_Style), ResourceType = typeof(GraphicsStrings))]
    public IProperty<StrokeStyles> Style { get; } = Property.CreateAnimatable(StrokeStyles.Background);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Pen is null)
            return;

        var data = (r.Offset, r.Pen, r.Style);
        // Forward is the pen+offset inflation; the stroke and the source draw are both contained in it, so backward
        // is identity (the FlatShadow pattern) — the geometry pass materializes the whole input regardless.
        builder.Geometry(GeometryNodeDescriptor.Create(
            session => ApplyGeometry(session, data),
            BoundsContract.Create(rect => TransformBounds(data, rect), static r => r),
            structuralToken: nameof(StrokeEffect)));
    }

    private static void ApplyGeometry(
        GeometrySession session, (Point Offset, Pen.Resource? Pen, StrokeStyles Style) data)
    {
        if (data.Pen is not { } pen)
            return;

        EffectInput input = session.Inputs[0];
        ImmediateCanvas newCanvas = session.OpenCanvas();
        using Bitmap src = input.Snapshot();

        // Contours come back in the input's device px; scale to logical so pen width and offset stay logical.
        float w = session.WorkingScale;
        using SKPath borderPath = CreateBorderPath(src);
        if (w != 1f)
            borderPath.Transform(SKMatrix.CreateScale(1f / w, 1f / w));

        Rect transformedBounds = session.Bounds;
        var origin = Matrix.CreateTranslation(
            input.Bounds.X - transformedBounds.X,
            input.Bounds.Y - transformedBounds.Y);

        using (newCanvas.PushTransform(origin))
        {
            if (data.Style == StrokeStyles.Background)
            {
                input.Draw(newCanvas);
            }

            using (newCanvas.PushTransform(Matrix.CreateTranslation(data.Offset.X, data.Offset.Y)))
            {
                newCanvas.DrawSKPath(borderPath, true, null, pen);
            }

            if (data.Style == StrokeStyles.Foreground)
            {
                input.Draw(newCanvas);
            }
        }
    }

    private static SKPath CreateBorderPath(Bitmap src)
    {
        using var contours = ContourTracer.FindContours(src);

        var skpath = new SKPath();
        foreach (var contour in contours)
        {
            for (int j = 0; j < contour.Count; j++)
            {
                if (j == 0)
                    skpath.MoveTo(contour[j].X, contour[j].Y);
                else
                    skpath.LineTo(contour[j].X, contour[j].Y);
            }

            skpath.Close();
        }

        return skpath;
    }

    private static Rect TransformBounds((Point Offset, Pen.Resource? Pen, StrokeStyles Style) data, Rect rect)
    {
        Rect borderBounds = PenHelper.GetBounds(rect, data.Pen);
        // Inflate symmetrically by offset so the source stays centered.
        return borderBounds.Inflate(new Thickness(
            Math.Abs(data.Offset.X), Math.Abs(data.Offset.Y),
            Math.Abs(data.Offset.X), Math.Abs(data.Offset.Y)));
    }
}
