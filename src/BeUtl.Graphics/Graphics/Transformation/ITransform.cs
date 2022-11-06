namespace Beutl.Graphics.Transformation;

public interface ITransform
{
    bool IsEnabled { get; }

    Matrix Value { get; }
}
