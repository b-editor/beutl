using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Graphics.Transformation;

public interface ITransform : IStyleable, IAffectsRender
{
    Matrix Value { get; }
}
