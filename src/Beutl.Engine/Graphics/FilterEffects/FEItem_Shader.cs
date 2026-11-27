using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Effects;

internal sealed record FEItem_Shader(ShaderDescription Description) : IFEItem
{
    public bool KeepsPlacement => Description.Kind == ShaderDescriptionKind.CurrentPixel;

    public Rect TransformBounds(Rect bounds) => Description.Bounds.TransformBounds(bounds);
}
