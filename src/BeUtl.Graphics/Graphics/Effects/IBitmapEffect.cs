using BeUtl.Media;

namespace BeUtl.Graphics.Effects;

public interface IBitmapEffect : IAffectsRender, ICoreObject
{
    bool IsEnabled { get; }

    IBitmapProcessor Processor { get; }

    Rect TransformBounds(Rect rect);
}

// Todo: IAnimatable, ICoreObject, IAffectsRender
