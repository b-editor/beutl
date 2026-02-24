using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.ColorShift), ResourceType = typeof(Strings))]
public partial class ColorShift : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<ColorShift>();
    private static readonly SKSLShader? s_shader;

    static ColorShift()
    {
        string sksl =
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

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public ColorShift()
    {
        ScanProperties<ColorShift>();
    }

    [Display(Name = nameof(Strings.RedOffset), ResourceType = typeof(Strings))]
    public IProperty<PixelPoint> RedOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(Strings.GreenOffset), ResourceType = typeof(Strings))]
    public IProperty<PixelPoint> GreenOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(Strings.BlueOffset), ResourceType = typeof(Strings))]
    public IProperty<PixelPoint> BlueOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(Strings.AlphaOffset), ResourceType = typeof(Strings))]
    public IProperty<PixelPoint> AlphaOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (s_shader is null)
        {
            throw new InvalidOperationException("Failed to compile SKSL.");
        }

        context.CustomEffect(
            (r.RedOffset, r.GreenOffset, r.BlueOffset, r.AlphaOffset),
            OnApply,
            TransformBoundsCore);
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

    private static void OnApply(
        (PixelPoint RedOffset, PixelPoint GreenOffset, PixelPoint BlueOffset, PixelPoint AlphaOffset) data,
        CustomFilterEffectContext context)
    {
        if (s_shader is null) return;
        for (int i = 0; i < context.Targets.Count; i++)
        {
            using var effectTarget = context.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;
            var bounds = TransformBoundsCore(data, effectTarget.Bounds);
            int minOffsetX = Math.Min(data.RedOffset.X,
                Math.Min(data.GreenOffset.X, Math.Min(data.BlueOffset.X, data.AlphaOffset.X)));
            int minOffsetY = Math.Min(data.RedOffset.Y,
                Math.Min(data.GreenOffset.Y, Math.Min(data.BlueOffset.Y, data.AlphaOffset.Y)));

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal);

            // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
            var builder = s_shader.CreateBuilder();

            // child shaderとしてテクスチャ用のシェーダーを設定
            builder.Children["src"] = baseShader;
            builder.Uniforms["redOffset"] = new SKPoint(data.RedOffset.X, data.RedOffset.Y);
            builder.Uniforms["greenOffset"] = new SKPoint(data.GreenOffset.X, data.GreenOffset.Y);
            builder.Uniforms["blueOffset"] = new SKPoint(data.BlueOffset.X, data.BlueOffset.Y);
            builder.Uniforms["alphaOffset"] = new SKPoint(data.AlphaOffset.X, data.AlphaOffset.Y);
            builder.Uniforms["minOffset"] = new SKPoint(minOffsetX, minOffsetY);

            // 新しいターゲットに適用
            context.Targets[i] = s_shader.ApplyToNewTarget(context, builder, bounds);
        }
    }
}
