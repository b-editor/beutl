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
        // matrix filter would use), so it is a per-operation geometry redraw. The render callback must derive the
        // pivot from the executed operation's own rect (session.Inputs[0]): the executor sizes every output buffer by
        // forward-mapping that same rect, so after an upstream fan-out a branch pivoted on the describe-time union
        // rotates out of its allocated buffer and vanishes. Backward inverts the same transform pivoted on the
        // describe-time input bounds (the resolver's whole-set view; over-claiming is safe, under-claiming crops):
        // an identity backward crops an upstream pass to the un-transformed region and loses the pixels the
        // rotation/scale pulls in (A3). FullFrame is unavailable here — the forward inflates the AABB, so it would
        // collapse the buffer to the input rect and clip the transformed content (the FlatShadow rationale).
        Rect inputBounds = builder.Bounds;
        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                Vector origin = originPoint.ToPixels(session.Inputs[0].Bounds.Size);
                Matrix offset = Matrix.CreateTranslation(origin);
                TransformGeometry.Render(session, (-offset) * mat * offset);
            },
            BoundsContract.Create(
                rect => ApplyToTargetBounds(rect, mat, originPoint),
                rect => InverseApplyToTargetBounds(rect, mat, originPoint, inputBounds)),
            structuralToken: nameof(TransformEffect) + ".ApplyToTarget"));
    }

    private static Rect ApplyToTargetBounds(Rect rect, Matrix mat, RelativePoint originPoint)
    {
        Vector origin = originPoint.ToPixels(rect.Size) + rect.Position;
        Matrix offset = Matrix.CreateTranslation(origin);
        return rect.TransformToAABB((-offset) * mat * offset);
    }

    private static Rect InverseApplyToTargetBounds(Rect rect, Matrix mat, RelativePoint originPoint, Rect inputBounds)
    {
        Vector origin = originPoint.ToPixels(inputBounds.Size) + inputBounds.Position;
        Matrix offset = Matrix.CreateTranslation(origin);
        Matrix transform = (-offset) * mat * offset;
        return transform.TryInvert(out Matrix inverted) ? rect.TransformToAABB(inverted) : Rect.Invalid;
    }

}
