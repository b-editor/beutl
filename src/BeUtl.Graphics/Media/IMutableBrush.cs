using BeUtl.Styling;

namespace BeUtl.Media;

public interface IMutableBrush : IBrush, IAffectsRender
{
    IBrush ToImmutable();
}
