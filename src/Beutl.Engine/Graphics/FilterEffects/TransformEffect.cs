using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;

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
                    // feature 003: source buffer is ceil(bounds × w) DEVICE px (At(w)); the transform math
                    // (origin pivot, m2, re-center translation) is all LOGICAL. Blit via the scale-aware
                    // target.Draw, which maps the device buffer back into its logical footprint. Otherwise
                    // logical-sized content on a w× device buffer lands off position and clips.
                    // w == 1 keeps the exact (int)-truncation point-blit (byte-identical).
                    effectContext.ForEach((_, target) =>
                    {
                        Vector origin = data.originPoint.ToPixels(target.Bounds.Size);
                        Matrix offset1 = Matrix.CreateTranslation(origin + target.Bounds.Position);
                        Matrix offset2 = Matrix.CreateTranslation(origin);
                        Matrix m1 = -offset1 * data.mat * offset1;
                        Matrix m2 = -offset2 * data.mat * offset2;

                        EffectTarget newTarget = effectContext.CreateTarget(target.Bounds.TransformToAABB(m1));
                        // feature 003: Open bakes the base CTM CreateScale(density) for this device buffer, with
                        // density read from the target so a TransformToAABB-inflated buffer that tripped the
                        // FR-037(b) clamp is honored. The LOGICAL transform placement then maps on automatically;
                        // density 1 stays byte-identical.
                        using var canvas = effectContext.Open(newTarget);
                        using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - newTarget.Bounds.Position)))
                        using (canvas.PushTransform(m2))
                        {
                            canvas.Clear();
                            target.Draw(canvas);
                        }

                        target.Dispose();
                        return newTarget;
                    });
                });
            }
        }
    }
}
