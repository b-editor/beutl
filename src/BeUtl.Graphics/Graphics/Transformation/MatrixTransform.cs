namespace Beutl.Graphics.Transformation;

public sealed class MatrixTransform : Transform
{
    public static readonly CoreProperty<Matrix> MatrixProperty;
    private Matrix _matrix;

    static MatrixTransform()
    {
        MatrixProperty = ConfigureProperty<Matrix, MatrixTransform>(nameof(Matrix))
            .Accessor(o => o.Matrix, (o, v) => o.Matrix = v)
            .DefaultValue(Matrix.Identity)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("matrix")
            .Register();

        AffectsRender<MatrixTransform>(MatrixProperty);
    }

    public MatrixTransform()
    {
        _matrix = Matrix.Identity;
    }

    public MatrixTransform(Matrix matrix)
    {
        Matrix = matrix;
    }

    public Matrix Matrix
    {
        get => _matrix;
        set => SetAndRaise(MatrixProperty, ref _matrix, value);
    }

    public override Matrix Value => Matrix;
}
