using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.TextOnPathEffect), Description = nameof(GraphicsStrings.TextOnPathEffect_LibraryDescription),
    ResourceType = typeof(GraphicsStrings))]
public sealed partial class TextOnPathEffect : FilterEffect
{
    public TextOnPathEffect()
    {
        ScanProperties<TextOnPathEffect>();

        const float baseline = 72;
        var figure = new PathFigure { StartPoint = { Value = new Point(0, baseline) } };
        figure.Segments.Add(new LineSegment(480, baseline));
        var path = new PathGeometry();
        path.Figures.Add(figure);
        Path.Value = path;
    }

    [Display(Name = nameof(GraphicsStrings.TextOnPathEffect_Path), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Geometry?> Path { get; } = Property.CreateExpression<Geometry?>();

    [Display(Name = nameof(GraphicsStrings.TextOnPathEffect_PathOffset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> PathOffset { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PathFollowEffect_FollowRotation), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> FollowRotation { get; } = Property.CreateAnimatable(true);

    private static void Apply((Geometry.Resource Path, float Offset, bool FollowRotation) settings,
        CustomFilterEffectContext context)
    {
        using var measure = new SKPathMeasure(settings.Path.GetCachedPath());
        float length = measure.Length;
        while (!(length > 0 && float.IsFinite(length)) && measure.NextContour())
            length = measure.Length;

        bool closed = measure.IsClosed;
        for (int i = context.Targets.Count - 1; i >= 0; i--)
        {
            EffectTarget target = context.Targets[i];
            if (target.TextPart is not { Positions: { } positions } tag)
                continue;

            tag = tag with { IncludeHiddenUnits = true };
            target.TextPart = tag;
            if (target.IsEmpty || tag.Indices.Character < 0)
                continue;

            TextPartPlacement placement = positions.Characters[tag.Indices.Character];
            float start = TextBlock.GetLineOffset(positions.Alignment, length, placement.LineLength);
            float distance = settings.Offset + start + placement.Distance;
            if (!TryPlace(measure, length, closed, distance, placement, settings.FollowRotation, out Matrix transform))
            {
                context.Targets.RemoveAt(i);
                continue;
            }

            Rect bounds = target.Bounds.TransformToAABB(transform);
            Matrix origin = Matrix.CreateTranslation(target.Bounds.Position.X, target.Bounds.Position.Y);
            Matrix local = origin * transform * -origin;
            if (TransformEffect.RedrawTransformed(context, target, bounds, local) is { } replacement)
            {
                if (target.CompleteBounds is { } completeBounds)
                    replacement.CompleteBounds = completeBounds.TransformToAABB(transform);
                context.Targets[i] = replacement;
            }
        }
    }

    private static bool TryPlace(SKPathMeasure measure, float length, bool closed, float distance,
        TextPartPlacement placement, bool followRotation, out Matrix transform)
    {
        transform = Matrix.Identity;
        // Skia clamps to the endpoints. Reject out-of-range centers before measuring each glyph.
        if (!(length > 0 && float.IsFinite(length) && float.IsFinite(distance))
            || distance < 0 || distance > length || (closed && distance == length)
            || !measure.GetPositionAndTangent(distance, out SKPoint position, out SKPoint tangent))
        {
            return false;
        }

        // Each line follows the same contour with its own offset along the normal.
        float x = position.X - tangent.Y * placement.LineOffset;
        float y = position.Y + tangent.X * placement.LineOffset;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(placement.Anchor.X) || !float.IsFinite(placement.Anchor.Y))
            return false;

        float angle = followRotation ? MathF.Atan2(tangent.Y, tangent.X) : 0;
        if (placement.Sideways)
            angle -= MathF.PI / 2;
        transform = Matrix.CreateTranslation(-placement.Anchor.X, -placement.Anchor.Y)
            * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(x, y);
        return true;
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (Path is { } path)
                context.CustomEffect((path, PathOffset, FollowRotation), Apply);
        }
    }
}
