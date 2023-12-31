namespace Beutl.Graphics.Rendering;

public sealed class TransformNode(Matrix transform, TransformOperator transformOperator) : ContainerNode
{
    public Matrix Transform { get; } = transform;

    public TransformOperator TransformOperator { get; } = transformOperator;

    protected override Rect TransformBounds(Rect bounds)
    {
        return bounds.TransformToAABB(Transform);
    }

    public bool Equals(Matrix transform, TransformOperator transformOperator)
    {
        return Transform == transform
            && TransformOperator == transformOperator;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        using (canvas.PushTransform(Transform, TransformOperator))
        {
            base.Render(canvas);
        }
    }

    // Todo: Append, Setの時の動作
    public override bool HitTest(Point point)
    {
        if (Transform.HasInverse)
            point *= Transform.Invert();
        return base.HitTest(point);
    }
}
