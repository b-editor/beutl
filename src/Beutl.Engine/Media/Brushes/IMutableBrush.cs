namespace Beutl.Media;

public interface IMutableBrush : IBrush, IAffectsRender
{
    IBrush ToImmutable();
}
