using System.ComponentModel.DataAnnotations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class MosaicEffect : FilterEffect
{
    public static readonly CoreProperty<Size> TileSizeProperty;
    public static readonly CoreProperty<RelativePoint> OriginProperty;
    private static readonly ILogger s_logger = Log.CreateLogger<MosaicEffect>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;
    private Size _tileSize = new(10, 10);
    private RelativePoint _origin = RelativePoint.Center;

    static MosaicEffect()
    {
        TileSizeProperty = ConfigureProperty<Size, MosaicEffect>(nameof(TileSize))
            .Accessor(o => o.TileSize, (o, v) => o.TileSize = v)
            .DefaultValue(new Size(10, 10))
            .Register();

        OriginProperty = ConfigureProperty<RelativePoint, MosaicEffect>(nameof(Origin))
            .Accessor(o => o.Origin, (o, v) => o.Origin = v)
            .DefaultValue(RelativePoint.Center)
            .Register();

        AffectsRender<MosaicEffect>(TileSizeProperty);
        string sksl =
            """
            uniform shader src;
            uniform float2 origin;
            uniform float2 tileSize;

            half4 main(float2 fragCoord) {
                float2 blockIndex = floor((fragCoord - origin) / tileSize);

                // タイルの中心位置を求める
                float2 sampleCoord = (blockIndex * tileSize + tileSize * 0.5) + origin;

                // 中心位置の色をサンプリングして返す
                return src.eval(sampleCoord);
            }
            """;

        // SKRuntimeEffectを使ってSKSLコードをコンパイル
        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    [Range(typeof(Size), "0.0001, 0.0001", "max,max")]
    public Size TileSize
    {
        get => _tileSize;
        set => SetAndRaise(TileSizeProperty, ref _tileSize, value);
    }

    public RelativePoint Origin
    {
        get => _origin;
        set => SetAndRaise(OriginProperty, ref _origin, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((TileSize, Origin), OnApplyTo, (_, r) => r);
    }

    private static void OnApplyTo((Size tileSize, RelativePoint origin) data, CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(image);

            // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

            // child shaderとしてテクスチャ用のシェーダーを設定
            builder.Children["src"] = baseShader;
            builder.Uniforms["tileSize"] = data.tileSize.ToSKSize();
            builder.Uniforms["origin"] = data.origin.ToPixels(new(image.Width, image.Height)).ToSKPoint();

            // 最終的なシェーダーを生成
            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint())
            {
                var newTarget = c.CreateTarget(effectTarget.Bounds);
                var canvas = newTarget.RenderTarget!.Value.Canvas;
                paint.Shader = finalShader;
                canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height), paint);

                c.Targets[i] = newTarget;
            }

            effectTarget.Dispose();
        }
    }
}
