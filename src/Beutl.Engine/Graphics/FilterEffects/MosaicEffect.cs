using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.MosaicEffect), ResourceType = typeof(GraphicsStrings))]
public partial class MosaicEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<MosaicEffect>();
    private static readonly SKSLShader? s_shader;

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

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public MosaicEffect()
    {
        ScanProperties<MosaicEffect>();
    }

    [Range(typeof(Size), "0.0001, 0.0001", "max,max")]
    [Display(Name = nameof(GraphicsStrings.MosaicEffect_TileSize), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Size> TileSize { get; } = Property.CreateAnimatable(new Size(10, 10));

    [Display(Name = nameof(GraphicsStrings.MosaicEffect_Origin), ResourceType = typeof(GraphicsStrings))]
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
        if (s_shader is null) return;

        for (int i = 0; i < c.Targets.Count; i++)
        {
            using var effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = image.ToShader();

            EffectTarget output = c.CreateTargetLike(effectTarget);
            try
            {
                var builder = s_shader.CreateBuilder();
                float w = output.Scale.Value;
                int bufferWidth = output.RenderTarget!.Width;
                int bufferHeight = output.RenderTarget.Height;
                builder.Uniforms["tileSize"] =
                    new Size(data.tileSize.Width * w, data.tileSize.Height * w).ToSKSize();
                Vector semanticOrigin = output.Bounds.Position - output.RasterBounds.Position;
                Point origin = data.origin.Unit == RelativeUnit.Relative
                    ? data.origin.ToPixels(new(bufferWidth, bufferHeight))
                    : data.origin.Point * w + semanticOrigin * w;
                builder.Uniforms["origin"] = origin.ToSKPoint();

                using SKShader mappedSource =
                    c.CreateMappedInputShader(effectTarget, output, baseShader);
                builder.Children["src"] = mappedSource;
                s_shader.RenderToTarget(c, builder, output);
                c.Targets[i] = output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }
}
