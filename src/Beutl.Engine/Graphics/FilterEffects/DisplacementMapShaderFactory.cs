using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal static class DisplacementMapShaderFactory
{
    public static SKShader CreateOrTransparent(
        CustomFilterEffectContext context,
        FilterEffectBrush brush,
        Rect bounds,
        float density)
        => context.CreateBrushShader(brush, bounds, BlendMode.SrcOver, density)
            ?? SKShader.CreateColor(SKColors.Transparent);
}
