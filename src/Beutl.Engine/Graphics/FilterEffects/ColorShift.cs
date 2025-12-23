using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public partial class ColorShift : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<ColorShift>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;

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

        // SKRuntimeEffectを使ってSKSLコードをコンパイル
        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public ColorShift()
    {
        ScanProperties<ColorShift>();
    }

    public IProperty<PixelPoint> RedOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    public IProperty<PixelPoint> GreenOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    public IProperty<PixelPoint> BlueOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    public IProperty<PixelPoint> AlphaOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (s_runtimeEffect is null)
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
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget effectTarget = context.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;
            var bounds = TransformBoundsCore(data, effectTarget.Bounds);
            var pixelRect = PixelRect.FromRect(bounds);
            int minOffsetX = Math.Min(data.RedOffset.X,
                Math.Min(data.GreenOffset.X, Math.Min(data.BlueOffset.X, data.AlphaOffset.X)));
            int minOffsetY = Math.Min(data.RedOffset.Y,
                Math.Min(data.GreenOffset.Y, Math.Min(data.BlueOffset.Y, data.AlphaOffset.Y)));

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(
                image, SKShaderTileMode.Decal, SKShaderTileMode.Decal);

            // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

            // child shaderとしてテクスチャ用のシェーダーを設定
            builder.Children["src"] = baseShader;
            builder.Uniforms["redOffset"] = new SKPoint(data.RedOffset.X, data.RedOffset.Y);
            builder.Uniforms["greenOffset"] = new SKPoint(data.GreenOffset.X, data.GreenOffset.Y);
            builder.Uniforms["blueOffset"] = new SKPoint(data.BlueOffset.X, data.BlueOffset.Y);
            builder.Uniforms["alphaOffset"] = new SKPoint(data.AlphaOffset.X, data.AlphaOffset.Y);
            builder.Uniforms["minOffset"] = new SKPoint(minOffsetX, minOffsetY);

            // 最終的なシェーダーを生成
            var newTarget = context.CreateTarget(bounds);
            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint())
            using (var canvas = context.Open(newTarget))
            {
                paint.Shader = finalShader;
                canvas.Canvas.DrawRect(new SKRect(0, 0, bounds.Width, bounds.Height), paint);

                context.Targets[i] = newTarget;
            }

            effectTarget.Dispose();
        }
    }
}
