using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Graphics.Transformation;

public interface ITransform : IAffectsRender
{
    Matrix Value { get; }
}

public interface IMutableTransform : IStyleable, ITransform
{
}
