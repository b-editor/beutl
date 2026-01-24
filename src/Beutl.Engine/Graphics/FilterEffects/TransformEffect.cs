using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
public sealed partial class TransformEffect : FilterEffect
{
    public TransformEffect()
    {
        ScanProperties<TransformEffect>();
    }

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>();

    [Display(Name = nameof(Strings.TransformOrigin), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(Strings.BitmapInterpolationMode), ResourceType = typeof(Strings))]
    public IProperty<BitmapInterpolationMode> BitmapInterpolationMode { get; } = Property.CreateAnimatable(Media.BitmapInterpolationMode.Default);

    public IProperty<bool> ApplyToTarget { get; } = Property.CreateAnimatable(true);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Transform != null)
        {
            var mat = r.Transform.Matrix;
            RelativePoint originPoint = r.TransformOrigin;

            if (!r.ApplyToTarget)
            {
                Vector origin = originPoint.ToPixels(context.Bounds.Size) + context.Bounds.Position;
                Matrix offset = Matrix.CreateTranslation(origin);

                Matrix transform = (-offset) * mat * offset;
                context.Transform(transform, r.BitmapInterpolationMode);
            }
            else
            {
                context.CustomEffect((mat, originPoint), static (data, effectContext) =>
                {
                    effectContext.ForEach((_, target) =>
                    {
                        Vector origin = data.originPoint.ToPixels(target.Bounds.Size);
                        Matrix offset1 = Matrix.CreateTranslation(origin + target.Bounds.Position);
                        Matrix offset2 = Matrix.CreateTranslation(origin);
                        Matrix m1 = -offset1 * data.mat * offset1;
                        Matrix m2 = -offset2 * data.mat * offset2;

                        EffectTarget newTarget = effectContext.CreateTarget(target.Bounds.TransformToAABB(m1));
                        using var canvas = effectContext.Open(newTarget);
                        using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - newTarget.Bounds.Position)))
                        using (canvas.PushTransform(m2))
                        {
                            canvas.Clear();
                            canvas.DrawRenderTarget(target.RenderTarget!, default);
                        }

                        target.Dispose();
                        return newTarget;
                    });
                });
            }
        }
    }
}
