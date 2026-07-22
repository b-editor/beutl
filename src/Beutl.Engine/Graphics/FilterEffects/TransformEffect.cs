using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings))]
public sealed partial class TransformEffect : FilterEffect
{
    public TransformEffect()
    {
        ScanProperties<TransformEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>();

    [Display(Name = nameof(GraphicsStrings.TransformOrigin), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(GraphicsStrings.TransformEffect_BitmapInterpolationMode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<BitmapInterpolationMode> BitmapInterpolationMode { get; } = Property.CreateAnimatable(Media.BitmapInterpolationMode.Default);

    public IProperty<bool> ApplyToTarget { get; } = Property.CreateAnimatable(true);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Transform != null)
        {
            var mat = r.Transform.Matrix;
            if (mat.IsIdentity)
                return;

            RelativePoint originPoint = r.TransformOrigin;

            if (!r.ApplyToTarget)
            {
                if (context.Bounds.IsInvalid)
                {
                    context.AppendSkiaFilter(
                        (mat, originPoint, r.BitmapInterpolationMode),
                        static (data, input, activator) =>
                        {
                            Rect bounds = activator.CurrentTargets.CalculateBounds();
                            Matrix transform = CreateRelativeOriginTransform(
                                data.mat,
                                data.originPoint,
                                bounds);
                            return SKImageFilter.CreateMatrix(
                                transform.ToSKMatrix(),
                                data.BitmapInterpolationMode.ToSKSamplingOptions(),
                                input);
                        },
                        static (data, bounds) => bounds.TransformToAABB(
                            CreateRelativeOriginTransform(data.mat, data.originPoint, bounds)));
                }
                else
                {
                    Vector origin = originPoint.ToPixels(context.Bounds.Size) + context.Bounds.Position;
                    Matrix offset = Matrix.CreateTranslation(origin);

                    Matrix transform = (-offset) * mat * offset;
                    context.Transform(transform, r.BitmapInterpolationMode);
                }
            }
            else
            {
                context.CustomEffect(
                    (mat, originPoint),
                    static (data, effectContext) =>
                    {
                        effectContext.ForEach((_, target) =>
                        {
                            Vector origin = data.originPoint.ToPixels(target.Bounds.Size);
                            Matrix offset = Matrix.CreateTranslation(origin);
                            Matrix transform = -offset * data.mat * offset;

                            EffectTarget newTarget = effectContext.CreateTarget(TransformBounds(data, target.Bounds));
                            using var canvas = effectContext.Open(newTarget);
                            using (canvas.PushTransform(Matrix.CreateTranslation(
                                       target.Bounds.Position - newTarget.Bounds.Position)))
                            using (canvas.PushTransform(transform))
                            {
                                canvas.Clear();
                                target.Draw(canvas);
                            }

                            target.Dispose();
                            return newTarget;
                        });
                    },
                    TransformBounds);
            }
        }
    }

    private static Rect TransformBounds((Matrix mat, RelativePoint originPoint) data, Rect bounds)
    {
        Vector origin = data.originPoint.ToPixels(bounds.Size);
        Matrix offset = Matrix.CreateTranslation(origin + bounds.Position);
        return bounds.TransformToAABB(-offset * data.mat * offset);
    }

    private static Matrix CreateRelativeOriginTransform(
        Matrix matrix,
        RelativePoint originPoint,
        Rect bounds)
    {
        Vector origin = originPoint.ToPixels(bounds.Size) + bounds.Position;
        Matrix offset = Matrix.CreateTranslation(origin);
        return (-offset) * matrix * offset;
    }
}
