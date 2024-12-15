using Beutl.Media;

namespace Beutl.Graphics3D.Transformation;

public interface IMutableTransform3D : ITransform3D, IAffectsRender
{
    ITransform3D ToImmutable();
}
