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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Transform == null)
            return;

        Matrix mat = r.Transform.Matrix;
        RelativePoint originPoint = r.TransformOrigin;

        if (!r.ApplyToTarget)
        {
            Vector origin = originPoint.ToPixels(builder.Bounds.Size) + builder.Bounds.Position;
            Matrix offset = Matrix.CreateTranslation(origin);
            Matrix transform = (-offset) * mat * offset;
            builder.Transform(transform, r.BitmapInterpolationMode);
            return;
        }

        // The ApplyToTarget path pivots each operation around its own centre (not the shared bounds centre a single
        // matrix filter would use), so it is a per-operation geometry redraw. In the linear single-input pipeline the
        // op's bounds equal the builder's; a fanned-out set (upstream split) still pivots each branch on its own rect.
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                Rect inRect = session.Inputs[0].Bounds;
                Vector origin = originPoint.ToPixels(inRect.Size);
                Matrix offset = Matrix.CreateTranslation(origin);
                TransformGeometry.Render(session, (-offset) * mat * offset);
            },
            BoundsContract.Create(rect => ApplyToTargetBounds(rect, mat, originPoint), static r => r),
            structuralToken: nameof(TransformEffect) + ".ApplyToTarget"));
    }

    private static Rect ApplyToTargetBounds(Rect rect, Matrix mat, RelativePoint originPoint)
    {
        Vector origin = originPoint.ToPixels(rect.Size) + rect.Position;
        Matrix offset = Matrix.CreateTranslation(origin);
        return rect.TransformToAABB((-offset) * mat * offset);
    }

}
