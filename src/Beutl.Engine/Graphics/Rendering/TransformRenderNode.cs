namespace Beutl.Graphics.Rendering;

public sealed class TransformRenderNode(Matrix transform, TransformOperator transformOperator) : ContainerRenderNode
{
    public Matrix Transform { get; private set; } = transform;

    public TransformOperator TransformOperator { get; private set; } = transformOperator;

    public bool Update(Matrix transform, TransformOperator transformOperator)
    {
        bool changed = false;
        if (Transform != transform)
        {
            Transform = transform;
            changed = true;
        }

        if (TransformOperator != transformOperator)
        {
            TransformOperator = transformOperator;
            changed = true;
        }

        HasChanges = changed;
        return changed;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input.Select(r =>
            RenderNodeOperation.CreateLambda(
                r.Bounds.TransformToAABB(Transform),
                canvas =>
                {
                    using (canvas.PushTransform(Transform, TransformOperator))
                    {
                        r.Render(canvas);
                    }
                },
                hitTest: point =>
                {
                    if (Transform.HasInverse)
                        point *= Transform.Invert();
                    return r.HitTest(point);
                },
                onDispose: r.Dispose,
                // feature 003: forward the child's supply density. A pure-CTM transform does not re-rasterize a
                // bitmap-backed child (a flushed effect buffer / image source), so the child stays At(d) rather
                // than the re-rasterizable Unbounded — the density it actually has, for a parent composite/effect
                // to reconcile correctly (FR-019). The density is NOT scaled by the transform (that would change
                // s_out == 1 output and break byte-identity); At(1)/Unbounded are identical at s_out == 1.
                effectiveScale: r.EffectiveScale))
            .ToArray();
    }
}
