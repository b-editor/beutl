using BeUtl.Styling;

namespace BeUtl.Media;

public interface IMutableBrush : IBrush, IStyleable, IAffectsRender
{
    IBrush ToImmutable();
}
