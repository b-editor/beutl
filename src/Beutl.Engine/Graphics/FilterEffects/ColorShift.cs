using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ColorShift), ResourceType = typeof(GraphicsStrings))]
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

            half4 main(float2 fragCoord) {
                // 出力画素座標 fragCoord に対し、各色成分のサンプル位置を計算
                float2 redCoord   = fragCoord - redOffset;
                float2 greenCoord = fragCoord - greenOffset;
                float2 blueCoord  = fragCoord - blueOffset;
                float2 alphaCoord = fragCoord - alphaOffset;

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

    [Display(Name = nameof(GraphicsStrings.ColorShift_RedOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> RedOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_GreenOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> GreenOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_BlueOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PixelPoint> BlueOffset { get; } = Property.CreateAnimatable<PixelPoint>();

    [Display(Name = nameof(GraphicsStrings.ColorShift_AlphaOffset), ResourceType = typeof(GraphicsStrings))]
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
            // Not `using`: the skip paths below keep this target in context.Targets[i], so it must
            // only be disposed after the slot is replaced with the shifted output.
            EffectTarget effectTarget = context.Targets[i];
            RenderTarget? renderTarget = effectTarget.RenderTarget;
            if (renderTarget is null)
            {
                continue;
            }

            var bounds = TransformBoundsCore(data, effectTarget.Bounds);

            using var image = renderTarget.Value.Snapshot();
            if (image is null)
            {
                // Delivery (MaxWorkingScale == +inf) must not silently ship an unshifted layer;
                // preview keeps the source pixels.
                if (float.IsPositiveInfinity(context.MaxWorkingScale))
                {
                    throw new InvalidOperationException(
                        $"ColorShift snapshot failed for target {i}; the GPU surface could not be read back.");
                }

                continue;
            }

            using var baseShader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal);

            EffectTarget output = bounds == effectTarget.Bounds
                ? context.CreateTargetLike(effectTarget)
                : context.CreateTarget(bounds);
            try
            {
                var builder = s_shader.CreateBuilder();
                float w = output.Scale.Value;
                builder.Uniforms["redOffset"] =
                    new SKPoint(data.RedOffset.X * w, data.RedOffset.Y * w);
                builder.Uniforms["greenOffset"] =
                    new SKPoint(data.GreenOffset.X * w, data.GreenOffset.Y * w);
                builder.Uniforms["blueOffset"] =
                    new SKPoint(data.BlueOffset.X * w, data.BlueOffset.Y * w);
                builder.Uniforms["alphaOffset"] =
                    new SKPoint(data.AlphaOffset.X * w, data.AlphaOffset.Y * w);

                using SKShader mappedSource =
                    context.CreateMappedInputShader(effectTarget, output, baseShader);
                builder.Children["src"] = mappedSource;
                s_shader.RenderToTarget(context, builder, output);
                context.Targets[i] = output;
                effectTarget.Dispose();
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }
}
