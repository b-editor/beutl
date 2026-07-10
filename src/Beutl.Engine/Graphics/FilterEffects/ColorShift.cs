using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorShift), ResourceType = typeof(GraphicsStrings))]
public partial class ColorShift : FilterEffect
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform float2 redOffset;
        uniform float2 greenOffset;
        uniform float2 blueOffset;
        uniform float2 alphaOffset;
        uniform float2 minOffset;

        half4 main(float2 fragCoord) {
            // 出力画素座標 fragCoord に対し、各色成分のサンプル位置を計算
            float2 redCoord   = fragCoord - redOffset   + minOffset;
            float2 greenCoord = fragCoord - greenOffset + minOffset;
            float2 blueCoord  = fragCoord - blueOffset  + minOffset;
            float2 alphaCoord = fragCoord - alphaOffset + minOffset;

            // 各色成分をそれぞれのオフセット位置からサンプル
            // ※ サンプラーは通常 RGBA 順で色成分を返します
            float red   = src.eval(redCoord).r;
            float green = src.eval(greenCoord).g;
            float blue  = src.eval(blueCoord).b;
            float alpha = src.eval(alphaCoord).a;

            return half4(red, green, blue, alpha);
        }
        """;

    public ColorShift()
    {
        ScanProperties<ColorShift>();
    }

    [Display(Name = nameof(GraphicsStrings.ColorShift_RedOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> RedOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_GreenOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> GreenOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_BlueOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> BlueOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_AlphaOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> AlphaOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var data = (r.RedOffset, r.GreenOffset, r.BlueOffset, r.AlphaOffset);

        // A whole-source shader sampling each channel from its own shifted position. The executor sizes this
        // non-invariant pass by its declared forward bounds (the union of the shifted copies) and re-bakes the
        // source into that expanded rect before the pass, so the shifted sample coordinate is already in the
        // output buffer's device space — the legacy minOffset alignment is absorbed by the re-bake and stays 0.
        // The device-space offsets are late-bound (DensityScaledFloat2): the forward-inflated buffer can cross the
        // 16384 px/axis budget and execute BELOW the describe-time working scale (execution-plan §C3.2), so the
        // logical offsets must be scaled by the pass's actual density, not the describe-time one.
        builder.Shader(ShaderNodeDescriptor.WholeSource(
            ShaderSource,
            BoundsContract.Create(
                rect => TransformBoundsCore(data, rect),
                rect => RequiredInputBoundsCore(data, rect)),
            u => u.DensityScaledFloat2("redOffset", data.RedOffset.X, data.RedOffset.Y)
                  .DensityScaledFloat2("greenOffset", data.GreenOffset.X, data.GreenOffset.Y)
                  .DensityScaledFloat2("blueOffset", data.BlueOffset.X, data.BlueOffset.Y)
                  .DensityScaledFloat2("alphaOffset", data.AlphaOffset.X, data.AlphaOffset.Y)
                  .Float2("minOffset", 0f, 0f),
            srcTileMode: SKShaderTileMode.Decal));
    }

    private static Rect RequiredInputBoundsCore(
        (PixelPoint RedOffset, PixelPoint GreenOffset, PixelPoint BlueOffset, PixelPoint AlphaOffset) data,
        Rect r)
    {
        return r.Translate(-data.RedOffset.ToPoint(1))
            .Union(r.Translate(-data.GreenOffset.ToPoint(1)))
            .Union(r.Translate(-data.BlueOffset.ToPoint(1)))
            .Union(r.Translate(-data.AlphaOffset.ToPoint(1)));
    }

    private static Rect TransformBoundsCore(
        (PixelPoint RedOffset, PixelPoint GreenOffset, PixelPoint BlueOffset, PixelPoint AlphaOffset) data,
        Rect bounds)
    {
        return bounds.Translate(data.RedOffset.ToPoint(1))
            .Union(bounds.Translate(data.GreenOffset.ToPoint(1)))
            .Union(bounds.Translate(data.BlueOffset.ToPoint(1)))
            .Union(bounds.Translate(data.AlphaOffset.ToPoint(1)));
    }
}
