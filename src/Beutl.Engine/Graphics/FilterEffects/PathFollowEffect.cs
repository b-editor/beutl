using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.PathFollowEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PathFollowEffect : FilterEffect
{
    public PathFollowEffect()
    {
        ScanProperties<PathFollowEffect>();
        Geometry.CurrentValue = new PathGeometry();
    }

    [Display(Name = nameof(GraphicsStrings.PathFollowEffect_Geometry), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Geometry?> Geometry { get; } = Property.Create<Geometry?>();

    [Display(Name = nameof(GraphicsStrings.PathFollowEffect_Progress), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Progress { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PathFollowEffect_FollowRotation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> FollowRotation { get; } = Property.CreateAnimatable(false);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Geometry == null)
            return;

        SKPath skPath = r.Geometry.GetCachedPath();
        if (skPath.IsEmpty)
            return;

        float progress = Math.Clamp(r.Progress, 0f, 100f) / 100f;
        using var pathMeasure = new SKPathMeasure(skPath);
        float totalLength = pathMeasure.Length;
        if (totalLength <= 0)
            return;

        if (!pathMeasure.GetPositionAndTangent(totalLength * progress, out SKPoint position, out SKPoint tangent))
            return;
        if (!pathMeasure.GetPosition(0, out SKPoint startPosition))
            return;

        var translate = Matrix.CreateTranslation(position.X - startPosition.X, position.Y - startPosition.Y);
        float rotationAngle = r.FollowRotation ? MathF.Atan2(tangent.Y, tangent.X) : 0f;

        // The pass translates (and optionally rotates) its input, so producing output region `rect` samples the input
        // over the inverse of the follow transform. The render callback pivots on the executed operation's own rect
        // (session.Inputs[0]), matching the forward map the executor sizes each output buffer with — after an
        // upstream fan-out a branch pivoted on the describe-time union rotates out of its allocated buffer. Backward
        // inverts the transform pivoted on the describe-time input bounds (the resolver's whole-set view); an
        // identity backward under-claims and crops an upstream pass to the un-followed region, losing the pixels the
        // follow motion pulls in (A3).
        Rect inputBounds = builder.Bounds;
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                Rect opBounds = session.Inputs[0].Bounds;
                var center = new Vector(opBounds.Width / 2, opBounds.Height / 2);
                TransformGeometry.Render(session, LocalMatrix(translate, rotationAngle, center));
            },
            BoundsContract.Create(
                rect => FollowBounds(rect, translate, rotationAngle),
                rect => InverseFollowBounds(rect, translate, rotationAngle, inputBounds)),
            structuralToken: nameof(PathFollowEffect)));
    }

    private static Matrix LocalMatrix(Matrix translate, float rotationAngle, Vector center)
    {
        if (rotationAngle == 0)
            return translate;

        Matrix offset = Matrix.CreateTranslation(center);
        return -offset * Matrix.CreateRotation(rotationAngle) * offset * translate;
    }

    private static Rect FollowBounds(Rect rect, Matrix translate, float rotationAngle)
    {
        if (rotationAngle == 0)
            return rect.TransformToAABB(translate);

        var center = new Vector(rect.Width / 2, rect.Height / 2);
        Matrix offset = Matrix.CreateTranslation(center + rect.Position);
        Matrix m1 = -offset * Matrix.CreateRotation(rotationAngle) * offset * translate;
        return rect.TransformToAABB(m1);
    }

    private static Rect InverseFollowBounds(Rect rect, Matrix translate, float rotationAngle, Rect inputBounds)
    {
        Matrix forward;
        if (rotationAngle == 0)
        {
            forward = translate;
        }
        else
        {
            var center = new Vector(inputBounds.Width / 2, inputBounds.Height / 2);
            Matrix offset = Matrix.CreateTranslation(center + inputBounds.Position);
            forward = -offset * Matrix.CreateRotation(rotationAngle) * offset * translate;
        }

        return forward.TryInvert(out Matrix inverted) ? rect.TransformToAABB(inverted) : Rect.Invalid;
    }

}
