using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Graphics.Effects;

public interface IBitmapEffect : IStyleable, IAffectsRender
{
    IBitmapProcessor Processor { get; }

    Rect TransformBounds(Rect rect);
}
