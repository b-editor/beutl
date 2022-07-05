using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Graphics.Transformation;

public interface ITransform
{
    bool IsEnabled { get; }

    Matrix Value { get; }
}

public interface IMutableTransform : IStyleable, ITransform, IAffectsRender
{
}
