using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class ColorShift : FilterEffect
{
    public static readonly CoreProperty<PixelPoint> RedOffsetProperty;
    public static readonly CoreProperty<PixelPoint> GreenOffsetProperty;
    public static readonly CoreProperty<PixelPoint> BlueOffsetProperty;
    public static readonly CoreProperty<PixelPoint> AlphaOffsetProperty;
    private static readonly ILogger s_logger = Log.CreateLogger<ColorShift>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;
    private PixelPoint _redOffset;
    private PixelPoint _greenOffset;
    private PixelPoint _blueOffset;
    private PixelPoint _alphaOffset;

    static ColorShift()
    {
        RedOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(RedOffset))
            .Accessor(o => o.RedOffset, (o, v) => o.RedOffset = v)
            .Register();

        GreenOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(GreenOffset))
            .Accessor(o => o.GreenOffset, (o, v) => o.GreenOffset = v)
            .Register();

        BlueOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(BlueOffset))
            .Accessor(o => o.BlueOffset, (o, v) => o.BlueOffset = v)
            .Register();

        AlphaOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(AlphaOffset))
            .Accessor(o => o.AlphaOffset, (o, v) => o.AlphaOffset = v)
            .Register();

        AffectsRender<ColorShift>(
            RedOffsetProperty, GreenOffsetProperty, BlueOffsetProperty, AlphaOffsetProperty);

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

    public PixelPoint RedOffset
    {
        get => _redOffset;
        set => SetAndRaise(RedOffsetProperty, ref _redOffset, value);
    }

    public PixelPoint GreenOffset
    {
        get => _greenOffset;
        set => SetAndRaise(GreenOffsetProperty, ref _greenOffset, value);
    }

    public PixelPoint BlueOffset
    {
        get => _blueOffset;
        set => SetAndRaise(BlueOffsetProperty, ref _blueOffset, value);
    }

    public PixelPoint AlphaOffset
    {
        get => _alphaOffset;
        set => SetAndRaise(AlphaOffsetProperty, ref _alphaOffset, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((RedOffset, GreenOffset, BlueOffset, AlphaOffset), OnApply, TransformBoundsCore);

        if (s_runtimeEffect is null) throw new InvalidOperationException("Failed to compile SKSL.");

        context.CustomEffect((RedOffset, GreenOffset, BlueOffset, AlphaOffset),
            OnApply, TransformBoundsCore);
    }

    private static Rect TransformBoundsCore(
        (PixelPoint RedOffset, PixelPoint GreenOffset, PixelPoint BlueOffset, PixelPoint AlphaOffset) d,
        Rect bounds)
    {
        return bounds.Translate(d.RedOffset.ToPoint(1))
            .Union(bounds.Translate(d.GreenOffset.ToPoint(1)))
            .Union(bounds.Translate(d.BlueOffset.ToPoint(1)))
            .Union(bounds.Translate(d.AlphaOffset.ToPoint(1)));
    }

    private static void OnApply(
        (PixelPoint RedOffset, PixelPoint GreenOffset, PixelPoint BlueOffset, PixelPoint AlphaOffset) d,
        CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;
            var bounds = TransformBoundsCore(d, effectTarget.Bounds);
            var pixelRect = PixelRect.FromRect(bounds);
            int minOffsetX = Math.Min(d.RedOffset.X,
                Math.Min(d.GreenOffset.X, Math.Min(d.BlueOffset.X, d.AlphaOffset.X)));
            int minOffsetY = Math.Min(d.RedOffset.Y,
                Math.Min(d.GreenOffset.Y, Math.Min(d.BlueOffset.Y, d.AlphaOffset.Y)));

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(
                image, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

            // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

            // child shaderとしてテクスチャ用のシェーダーを設定
            builder.Children["src"] = baseShader;
            builder.Uniforms["redOffset"] = new SKPoint(d.RedOffset.X, d.RedOffset.Y);
            builder.Uniforms["greenOffset"] = new SKPoint(d.GreenOffset.X, d.GreenOffset.Y);
            builder.Uniforms["blueOffset"] = new SKPoint(d.BlueOffset.X, d.BlueOffset.Y);
            builder.Uniforms["alphaOffset"] = new SKPoint(d.AlphaOffset.X, d.AlphaOffset.Y);
            builder.Uniforms["minOffset"] = new SKPoint(minOffsetX, minOffsetY);

            // 最終的なシェーダーを生成
            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint())
            {
                var newTarget = c.CreateTarget(bounds);
                var canvas = newTarget.RenderTarget!.Value.Canvas;
                paint.Shader = finalShader;
                canvas.DrawRect(new SKRect(0, 0, bounds.Width, bounds.Height), paint);

                c.Targets[i] = newTarget;
            }

            effectTarget.Dispose();
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return TransformBoundsCore((RedOffset, GreenOffset, BlueOffset, AlphaOffset), bounds);
    }
}
