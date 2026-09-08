namespace Beutl.Graphics.Effects;

internal sealed record FEItem_Geometry(GeometryDescription Description) : IFEItem
{
    public Rect TransformBounds(Rect bounds) => Description.Bounds.TransformBounds(bounds);
}
