using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(Strings.MatrixTransform), ResourceType = typeof(Strings))]
public sealed class MatrixTransform : Transform
{
    public MatrixTransform()
    {
        ScanProperties<MatrixTransform>();
    }

    public MatrixTransform(Matrix matrix) : this()
    {
        Matrix.CurrentValue = matrix;
    }

    [Display(Name = nameof(Strings.Matrix), ResourceType = typeof(Strings))]
    public IProperty<Matrix> Matrix { get; } = Property.CreateAnimatable(Graphics.Matrix.Identity);

    public override Matrix CreateMatrix(RenderContext context)
    {
        return context.Get(Matrix);
    }
}
