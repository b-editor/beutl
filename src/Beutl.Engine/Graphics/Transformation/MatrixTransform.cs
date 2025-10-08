using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Transformation;

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

    public IProperty<Matrix> Matrix { get; } = Property.CreateAnimatable(Graphics.Matrix.Identity);

    public override Matrix CreateMatrix(RenderContext context)
    {
        return context.Get(Matrix);
    }
}
