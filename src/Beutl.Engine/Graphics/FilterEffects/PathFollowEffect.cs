using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
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

        float distance = totalLength * progress;

        if (!pathMeasure.GetPositionAndTangent(distance, out SKPoint position, out SKPoint tangent))
            return;

        if (!pathMeasure.GetPosition(0, out SKPoint startPosition))
            return;

        float offsetX = position.X - startPosition.X;
        float offsetY = position.Y - startPosition.Y;

        float rotationAngle = 0f;
        if (r.FollowRotation)
        {
            rotationAngle = MathF.Atan2(tangent.Y, tangent.X);
        }

        context.CustomEffect((offsetX, offsetY, rotationAngle), Apply, TransformBounds);
    }

    private static void Apply(
        (float offsetX, float offsetY, float rotationAngle) data,
        CustomFilterEffectContext effectContext)
    {
        effectContext.ForEach((_, target) =>
        {
            CreateTransforms(data, target.Bounds, out Matrix boundsTransform, out Matrix contentTransform);
            Rect newBounds = target.Bounds.TransformToAABB(boundsTransform);
            EffectTarget newTarget = effectContext.CreateTarget(newBounds);
            // Open bakes the base CTM from the target's density.
            using (var canvas = effectContext.Open(newTarget))
            using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - newTarget.Bounds.Position)))
            using (canvas.PushTransform(contentTransform))
            {
                canvas.Clear();
                target.Draw(canvas);
            }

            target.Dispose();
            return newTarget;
        });
    }

    private static Rect TransformBounds(
        (float offsetX, float offsetY, float rotationAngle) data,
        Rect bounds)
    {
        CreateTransforms(data, bounds, out Matrix boundsTransform, out _);
        Rect result = bounds.TransformToAABB(boundsTransform);

        if (data.rotationAngle != 0)
        {
            // Apply rotates every EffectTarget around that target's own center. Bounds is only
            // their union, so rotating the union around its center is not sufficient when the
            // targets are separated. The center-dependent translation is (I - R) * deltaCenter;
            // inflate by its maximum projection over every center inside the union.
            float sin = MathF.Abs(MathF.Sin(data.rotationAngle));
            float oneMinusCos = MathF.Abs(1f - MathF.Cos(data.rotationAngle));
            float horizontal = ((oneMinusCos * bounds.Width) + (sin * bounds.Height)) / 2f;
            float vertical = ((sin * bounds.Width) + (oneMinusCos * bounds.Height)) / 2f;
            result = result.Inflate(new Thickness(horizontal, vertical));
        }

        return RenderRectValidation.IsFiniteNonNegative(result) ? result : Rect.Invalid;
    }

    private static void CreateTransforms(
        (float offsetX, float offsetY, float rotationAngle) data,
        Rect bounds,
        out Matrix boundsTransform,
        out Matrix contentTransform)
    {
        Matrix translate = Matrix.CreateTranslation(data.offsetX, data.offsetY);
        if (data.rotationAngle != 0)
        {
            var center = new Vector(bounds.Width / 2, bounds.Height / 2);
            Matrix rotate = Matrix.CreateRotation(data.rotationAngle);
            Matrix boundsOrigin = Matrix.CreateTranslation(center + bounds.Position);
            Matrix contentOrigin = Matrix.CreateTranslation(center);
            boundsTransform = -boundsOrigin * rotate * boundsOrigin * translate;
            contentTransform = -contentOrigin * rotate * contentOrigin * translate;
        }
        else
        {
            boundsTransform = contentTransform = translate;
        }
    }
}
