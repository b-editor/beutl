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
    public IExpressionProperty<Transform?> Transform { get; } = Property.CreateExpression<Transform?>();

    [Display(Name = nameof(GraphicsStrings.TransformOrigin), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(GraphicsStrings.TransformEffect_BitmapInterpolationMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<BitmapInterpolationMode> BitmapInterpolationMode { get; } = Property.CreateAnimatable(Media.BitmapInterpolationMode.Default);

    [Display(Name = nameof(GraphicsStrings.TransformEffect_ApplyToTarget), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> ApplyToTarget { get; } = Property.CreateAnimatable(true);

    /// <summary>
    /// Redraws each target through the transform applied about its own origin point; a target the transform
    /// maps to nothing is dropped.
    /// </summary>
    private static void TransformTargets(
        (Matrix mat, RelativePoint originPoint) data,
        CustomFilterEffectContext effectContext)
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
                i--;
                continue;
            }

            if (RedrawTransformed(effectContext, target, newBounds, m2) is not { } newTarget)
                continue;

            effectContext.Targets[i] = newTarget;
        }
    }

    /// <summary>
    /// Draws <paramref name="target"/> through <paramref name="transform"/> into a new target over
    /// <paramref name="bounds"/>, or returns <see langword="null"/> when that target cannot be allocated.
    /// </summary>
    internal static EffectTarget? RedrawTransformed(
        CustomFilterEffectContext context,
        EffectTarget target,
        Rect bounds,
        Matrix transform)
    {
        EffectTarget newTarget = context.CreateTarget(bounds);
        if (newTarget.IsEmpty)
        {
            newTarget.Dispose();
            return null;
        }

        // Open bakes the base CTM from the target's density.
        using (ImmediateCanvas canvas = context.Open(newTarget))
        using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - newTarget.Bounds.Position)))
        using (canvas.PushTransform(transform))
        {
            canvas.Clear();
            target.Draw(canvas);
        }

        return newTarget;
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (Transform != null)
            {
                var mat = Transform.Matrix;
                RelativePoint originPoint = TransformOrigin;

                if (!ApplyToTarget)
                {
                    context.Transform(
                        (mat, originPoint),
                        static (data, bounds) =>
                        {
                            Vector origin = data.originPoint.ToPixels(bounds.Size) + bounds.Position;
                            Matrix offset = Matrix.CreateTranslation(origin);
                            return (-offset) * data.mat * offset;
                        },
                        BitmapInterpolationMode);
                }
                else
                {
                    context.CustomEffect((mat, originPoint), TransformTargets);
                }
            }
        }
    }
}
