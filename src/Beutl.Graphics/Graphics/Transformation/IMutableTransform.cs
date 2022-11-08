using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Graphics.Transformation;

public interface IMutableTransform : ICoreObject, ITransform, IAffectsRender, IAnimatable
{
    ITransform ToImmutable();
}
