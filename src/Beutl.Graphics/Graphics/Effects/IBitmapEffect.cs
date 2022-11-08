namespace Beutl.Graphics.Effects;

public interface IBitmapEffect
{
    bool IsEnabled { get; }

    IBitmapProcessor Processor { get; }

    Rect TransformBounds(Rect rect);
}
