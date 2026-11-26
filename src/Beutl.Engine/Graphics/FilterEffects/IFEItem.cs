namespace Beutl.Graphics.Effects;

internal interface IFEItem
{
    // Whether the operation leaves its input in the same logical coordinates, even if it changes the bounds.
    bool KeepsPlacement => false;

    Rect TransformBounds(Rect bounds);
}
