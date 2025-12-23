using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public partial class MosaicEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<MosaicEffect>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;

    static MosaicEffect()
    {
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

    public MosaicEffect()
    {
        ScanProperties<MosaicEffect>();
    }

    [Range(typeof(Size), "0.0001, 0.0001", "max,max")]
    [Display(Name = nameof(Strings.TileSize), ResourceType = typeof(Strings))]
    public IProperty<Size> TileSize { get; } = Property.CreateAnimatable(new Size(10, 10));

    [Display(Name = nameof(Strings.Origin), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> Origin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect(
            (r.TileSize, r.Origin),
            OnApplyTo,
            static (_, r) => r);
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
            var newTarget = c.CreateTarget(effectTarget.Bounds);
            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint())
            using (var canvas = c.Open(newTarget))
            {
                paint.Shader = finalShader;
                canvas.Clear();
                canvas.Canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height), paint);

                c.Targets[i] = newTarget;
            }

            effectTarget.Dispose();
        }
    }
}
