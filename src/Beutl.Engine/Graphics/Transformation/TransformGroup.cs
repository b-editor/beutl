using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Transformation;

public sealed class TransformGroup : Transform
{
    public TransformGroup()
    {
        Children = new Transforms(this);
    }

    public Transforms Children { get; }

    public override Matrix CreateMatrix(RenderContext context)
    {
        return Children.Where(item => item.IsEnabled)
            .Aggregate(Matrix.Identity, (current, item) => item.CreateMatrix(context) * current);
    }
}
