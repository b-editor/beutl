namespace Beutl.Graphics.Effects;

internal sealed record FEItem_Shader(ShaderDescription Description) : IFEItem
{
    public Rect TransformBounds(Rect bounds) => Description.Bounds.TransformBounds(bounds);
}
