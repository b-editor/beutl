using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(GraphicsStrings.MatrixTransform), ResourceType = typeof(GraphicsStrings))]
public sealed partial class MatrixTransform : Transform
{
    public MatrixTransform()
    {
        ScanProperties<MatrixTransform>();
    }

    public MatrixTransform(Matrix matrix) : this()
    {
        Matrix.Value = matrix;
    }

    [Display(Name = nameof(GraphicsStrings.MatrixTransform_Matrix), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Matrix> Matrix { get; } = Property.CreateAnimatable(Graphics.Matrix.Identity);

    public override Matrix CreateMatrix(CompositionContext context)
    {
        return context.Get(Matrix);
    }
}
