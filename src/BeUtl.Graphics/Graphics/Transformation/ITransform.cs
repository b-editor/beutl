using BeUtl.Media;

namespace BeUtl.Graphics.Transformation;

public interface ITransform
{
    bool IsEnabled { get; }

    Matrix Value { get; }
}

// Todo: IAnimatable
public interface IMutableTransform : ICoreObject, ITransform, IAffectsRender
{
}
