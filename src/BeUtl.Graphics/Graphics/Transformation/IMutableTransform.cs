using BeUtl.Media;

namespace BeUtl.Graphics.Transformation;

// Todo: IAnimatable
public interface IMutableTransform : ICoreObject, ITransform, IAffectsRender
{
    ITransform ToImmutable();
}
