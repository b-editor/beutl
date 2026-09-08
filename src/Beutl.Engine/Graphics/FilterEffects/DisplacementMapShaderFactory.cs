using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal static class DisplacementMapShaderFactory
{
    public static SKShader CreateOrTransparent(
        CustomFilterEffectContext context,
        Brush.Resource? brush,
        Rect bounds,
        float density)
        => context.CreateBrushConstructor(bounds, brush, BlendMode.SrcOver, density).CreateShader()
            ?? SKShader.CreateColor(SKColors.Transparent);
}
