using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(GraphicsStrings.TransformGroup), ResourceType = typeof(GraphicsStrings))]
public sealed class TransformGroup : Transform
{
    public TransformGroup()
    {
        ScanProperties<TransformGroup>();
    }

    public IListProperty<Transform> Children { get; } = Property.CreateList<Transform>();

    public override Matrix CreateMatrix(CompositionContext context)
    {
        return Children.Where(item => item.IsEnabled)
            .Aggregate(Matrix.Identity, (current, item) => item.CreateMatrix(context) * current);
    }
}
