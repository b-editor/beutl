using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public class ImmutableTransform : ITransform
{
    public Matrix Value { get; }

    public bool IsEnabled { get; }

    public ImmutableTransform(Matrix matrix, bool isEnabled = true)
    {
        Value = matrix;
        IsEnabled = isEnabled;
    }
}
