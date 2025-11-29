using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Transformation;

public sealed class TransformGroup : Transform
{
    public TransformGroup()
    {
        ScanProperties<TransformGroup>();
    }

    public IListProperty<Transform> Children { get; } = Property.CreateList<Transform>();

    public override Matrix CreateMatrix(RenderContext context)
    {
        return Children.Where(item => item.IsEnabled)
            .Aggregate(Matrix.Identity, (current, item) => item.CreateMatrix(context) * current);
    }
}
