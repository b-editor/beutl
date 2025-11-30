using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class PathFollowEffect : FilterEffect
{
    public PathFollowEffect()
    {
        ScanProperties<PathFollowEffect>();
        Geometry.CurrentValue = new PathGeometry();
    }

    [Display(Name = nameof(Strings.Geometry), ResourceType = typeof(Strings))]
    public IProperty<Geometry?> Geometry { get; } = Property.Create<Geometry?>();

    [Display(Name = nameof(Strings.Progress), ResourceType = typeof(Strings))]
    public IProperty<float> Progress { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(Strings.FollowRotation), ResourceType = typeof(Strings))]
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

        context.CustomEffect((offsetX, offsetY, rotationAngle), static (data, effectContext) =>
        {
            effectContext.ForEach((_, target) =>
            {

                var translate = Matrix.CreateTranslation(data.offsetX, data.offsetY);
                Matrix m1, m2;
                if (data.rotationAngle != 0)
                {
                    var center = new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
                    var rotate = Matrix.CreateRotation(data.rotationAngle);

                    var offset1 = Matrix.CreateTranslation(center + target.Bounds.Position);
                    var offset2 = Matrix.CreateTranslation(center);
                    m1 = -offset1 * rotate * offset1 * translate;
                    m2 = -offset2 * rotate * offset2 * translate;
                }
                else
                {
                    m1 = m2 = translate;
                }

                var newBounds = target.Bounds.TransformToAABB(m1);
                var newTarget = effectContext.CreateTarget(newBounds);
                using (var canvas = effectContext.Open(newTarget))
                using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - newTarget.Bounds.Position)))
                using (canvas.PushTransform(m2))
                {
                    target.Draw(canvas);
                }

                target.Dispose();
                return newTarget;
            });
        });
    }
}
