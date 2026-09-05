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
                context.Transform(
                    (mat, originPoint),
                    static (data, bounds) =>
                    {
                        Vector origin = data.originPoint.ToPixels(bounds.Size) + bounds.Position;
                        Matrix offset = Matrix.CreateTranslation(origin);
                        return (-offset) * data.mat * offset;
                    },
                    r.BitmapInterpolationMode);
            }
            else
            {
                context.CustomEffect((mat, originPoint), static (data, effectContext) =>
                {
                    for (int i = 0; i < effectContext.Targets.Count; i++)
                    {
                        EffectTarget target = effectContext.Targets[i];
                        Vector origin = data.originPoint.ToPixels(target.Bounds.Size);
                        Matrix offset1 = Matrix.CreateTranslation(origin + target.Bounds.Position);
                        Matrix offset2 = Matrix.CreateTranslation(origin);
                        Matrix m1 = -offset1 * data.mat * offset1;
                        Matrix m2 = -offset2 * data.mat * offset2;

                        // An empty box here is the transform's answer, not the allocation failure below:
                        // handing the source back would show the layer untransformed.
                        Rect newBounds = target.Bounds.TransformToDeliveredAABB(m1, effectContext.TargetDomain);
                        if (newBounds.IsEmpty)
                        {
                            effectContext.Targets.RemoveAt(i);
                            target.Dispose();
                            i--;
                            continue;
                        }

                        EffectTarget newTarget = effectContext.CreateTarget(newBounds);
                        if (newTarget.IsEmpty)
                        {
                            newTarget.Dispose();
                            continue;
                        }

                        using (ImmediateCanvas canvas = effectContext.Open(newTarget))
                        using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - newTarget.Bounds.Position)))
                        using (canvas.PushTransform(m2))
                        {
                            canvas.Clear();
                            target.Draw(canvas);
                        }

                        effectContext.Targets[i] = newTarget;
                        target.Dispose();
                    }
                });
            }
        }
    }
}
