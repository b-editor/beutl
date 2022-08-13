using BeUtl.Animation;
using BeUtl.Media;

namespace BeUtl.Graphics.Transformation;

public interface IMutableTransform : ICoreObject, ITransform, IAffectsRender, IAnimatable
{
    ITransform ToImmutable();
}
