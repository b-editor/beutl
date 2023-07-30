namespace Beutl.Graphics.Rendering;

public sealed class TransformNode : ContainerNode
{
    public TransformNode(Matrix transform, TransformOperator transformOperator)
    {
        Transform = transform;
        TransformOperator = transformOperator;
    }

    public Matrix Transform { get; }

    public TransformOperator TransformOperator { get; }

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
