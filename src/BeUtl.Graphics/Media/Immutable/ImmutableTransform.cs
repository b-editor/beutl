using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;

namespace BeUtl.Media.Immutable;

public class ImmutableTransform : ITransform
{
    public Matrix Value { get; }

    public bool IsEnabled { get; }

    public ImmutableTransform(Matrix matrix, bool isEnabled = true)
    {
        Value = matrix;
        IsEnabled = isEnabled;
    }

    public event EventHandler? Invalidated;
}
