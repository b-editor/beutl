namespace Beutl.Graphics.Rendering;

public sealed class TransformRenderNode(Matrix transform, TransformOperator transformOperator) : ContainerRenderNode
{
    public Matrix Transform { get; } = transform;

    public TransformOperator TransformOperator { get; } = transformOperator;

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
                onDispose: r.Dispose))
            .ToArray();
    }

    public bool Equals(Matrix transform, TransformOperator transformOperator)
    {
        return Transform == transform
               && TransformOperator == transformOperator;
    }
}
