using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.MosaicEffect), ResourceType = typeof(GraphicsStrings))]
public partial class MosaicEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<MosaicEffect>();
    private static readonly SKSLShader? s_shader;

    private const string ShaderSource =
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

    static MosaicEffect()
    {
        if (!SKSLShader.TryCreate(ShaderSource, out s_shader, out string? errorText))
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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (s_shader is null)
            return;

        Size tileSize = r.TileSize;
        RelativePoint originPoint = r.Origin;
        float w = builder.WorkingScale;
        (int bufW, int bufH) = CustomFilterEffectContext.DeviceBufferSize(builder.Bounds, w);
        Point origin = originPoint.Unit == RelativeUnit.Relative
            ? originPoint.ToPixels(new Size(bufW, bufH))
            : originPoint.Point * w;

        // A whole-source shader that samples its tile centre (non-invariant), but its output occupies the input rect,
        // so the bounds contract is identity — exactly the legacy CustomEffect's transformBounds.
        builder.Shader(ShaderNodeDescriptor.WholeSource(
            ShaderSource,
            BoundsContract.Identity,
            u => u.Float2("origin", (float)origin.X, (float)origin.Y)
                  .Float2("tileSize", tileSize.Width * w, tileSize.Height * w)));
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

            // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
            var builder = s_shader.CreateBuilder();

            // child shaderとしてテクスチャ用のシェーダーを設定
            builder.Children["src"] = baseShader;
            // Scale tile size by working density so uniforms match the device-px buffer.
            float w = c.ResolveTargetDensity(effectTarget.Bounds);
            var (bufW, bufH) = CustomFilterEffectContext.DeviceBufferSize(effectTarget.Bounds, w);
            builder.Uniforms["tileSize"] = new Size(data.tileSize.Width * w, data.tileSize.Height * w).ToSKSize();
            Point origin = data.origin.Unit == RelativeUnit.Relative
                ? data.origin.ToPixels(new(bufW, bufH))
                : data.origin.Point * w;
            builder.Uniforms["origin"] = origin.ToSKPoint();

            // 新しいターゲットに適用
            c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
        }
    }
}
