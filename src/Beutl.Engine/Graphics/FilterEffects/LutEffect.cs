using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LutEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LutEffect : FilterEffect
{
    private static readonly ILogger<LutEffect> s_logger =
        BeutlApplication.Current.LoggerFactory.CreateLogger<LutEffect>();

    private static readonly SKSLShader? s_shader;
    private static readonly SKSLShader? s_1dShader;

    static LutEffect()
    {
        // https://shizenkarasuzon.hatenablog.com/entry/2020/08/13/185223
        string sksl =
            """
            uniform shader src;
            // 横に長いシェーダー指定
            uniform shader lut;
            uniform int lutSize;
            uniform float strength;

            int modInt(int a, int b) {
                return a - b * (a / b);
            }

            float3 trilinear_interpolate(float3 color)
            {
                int3 pos; // 0~33
                float3 delta; //
                int lutSize2 = lutSize * lutSize;

                pos.x = int(clamp((color.r * 255.0) * float(lutSize) / 256.0, 0, 255));
                pos.y = int(clamp((color.g * 255.0) * float(lutSize) / 256.0, 0, 255));
                pos.z = int(clamp((color.b * 255.0) * float(lutSize) / 256.0, 0, 255));

                // 小数点部分
                delta.x = ((color.r * 255.0) * float(lutSize) / 256.0) - float(pos.x);
                delta.y = ((color.g * 255.0) * float(lutSize) / 256.0) - float(pos.y);
                delta.z = ((color.b * 255.0) * float(lutSize) / 256.0) - float(pos.z);

                float3 vertex_color_0, vertex_color_1, vertex_color_2, vertex_color_3, vertex_color_4, vertex_color_5, vertex_color_6, vertex_color_7;
                float3 surf_color_0, surf_color_1, surf_color_2, surf_color_3;
                float3 line_color_0, line_color_1;
                float3 out_color;

                int index = pos.x + pos.y * lutSize + pos.z * lutSize2;

                int next_index_0 = 1;
                int next_index_1 = lutSize;
                int next_index_2 = lutSize2;

                if (modInt(index, lutSize) == lutSize - 1)
                {
                    next_index_0 = 0;
                }
                if (modInt(index / lutSize, lutSize) == lutSize - 1)
                {
                    next_index_1 = 0;
                }
                if (modInt(index / lutSize2, lutSize) == lutSize - 1)
                {
                    next_index_2 = 0;
                }

                // https://en.wikipedia.org/wiki/Trilinear_interpolation
                vertex_color_0 = float3(lut.eval(float2(index, 0)).rgb);
                vertex_color_1 = float3(lut.eval(float2(index + next_index_0, 0)).rgb);
                vertex_color_2 = float3(lut.eval(float2(index + next_index_0 + next_index_1, 0)).rgb);
                vertex_color_3 = float3(lut.eval(float2(index + next_index_1, 0)).rgb);
                vertex_color_4 = float3(lut.eval(float2(index + next_index_2, 0)).rgb);
                vertex_color_5 = float3(lut.eval(float2(index + next_index_0 + next_index_2, 0)).rgb);
                vertex_color_6 = float3(lut.eval(float2(index + next_index_0 + next_index_1 + next_index_2, 0)).rgb);
                vertex_color_7 = float3(lut.eval(float2(index + next_index_1 + next_index_2, 0)).rgb);

                surf_color_0 = vertex_color_0 * (1.0 - delta.z) + vertex_color_4 * delta.z;
                surf_color_1 = vertex_color_1 * (1.0 - delta.z) + vertex_color_5 * delta.z;
                surf_color_2 = vertex_color_2 * (1.0 - delta.z) + vertex_color_6 * delta.z;
                surf_color_3 = vertex_color_3 * (1.0 - delta.z) + vertex_color_7 * delta.z;

                line_color_0 = surf_color_0 * (1.0 - delta.x) + surf_color_1 * delta.x;
                line_color_1 = surf_color_3 * (1.0 - delta.x) + surf_color_2 * delta.x;

                out_color = line_color_0 * (1.0 - delta.y) + line_color_1 * delta.y;

                return out_color;
            }

            // リニアsRGB → sRGBガンマ変換
            float3 linearToSrgb(float3 c) {
                float3 lo = c * 12.92;
                float3 hi = 1.055 * pow(c, float3(1.0/2.4)) - 0.055;
                return mix(lo, hi, step(float3(0.0031308), c));
            }

            // sRGBガンマ → リニアsRGB変換
            float3 srgbToLinear(float3 c) {
                float3 lo = c / 12.92;
                float3 hi = pow((c + 0.055) / 1.055, float3(2.4));
                return mix(lo, hi, step(float3(0.04045), c));
            }

            half4 main(float2 fragCoord) {
                float4 c = float4(src.eval(fragCoord));

                // プリマルチプライドアルファを解除
                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                // リニア→sRGBに変換してからLUT適用（LUTはsRGB前提）
                float3 srgbColor = linearToSrgb(rgb);
                float3 lutResult = trilinear_interpolate(srgbColor);

                // LUT結果をsRGB→リニアに戻す
                lutResult = srgbToLinear(lutResult);

                // strengthで混合（リニア空間で）
                float3 result = mix(rgb, lutResult, strength);

                // プリマルチプライドアルファに戻す
                return half4(half3(result * alpha), half(alpha));
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }

        // 1D LUT SkSLシェーダー（バイトテーブルの代替）
        string sksl1d =
            """
            uniform shader src;
            uniform shader lut;
            uniform int lutSize;
            uniform float strength;

            float3 linearToSrgb(float3 c) {
                float3 lo = c * 12.92;
                float3 hi = 1.055 * pow(max(c, float3(0.0)), float3(1.0/2.4)) - 0.055;
                return mix(lo, hi, step(float3(0.0031308), c));
            }

            float3 srgbToLinear(float3 c) {
                float3 lo = c / 12.92;
                float3 hi = pow((c + 0.055) / 1.055, float3(2.4));
                return mix(lo, hi, step(float3(0.04045), c));
            }

            half4 main(float2 fragCoord) {
                float4 c = float4(src.eval(fragCoord));

                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 srgbColor = linearToSrgb(rgb);

                float maxIdx = float(lutSize - 1);
                float rIdx = clamp(srgbColor.r, 0.0, 1.0) * maxIdx;
                float gIdx = clamp(srgbColor.g, 0.0, 1.0) * maxIdx;
                float bIdx = clamp(srgbColor.b, 0.0, 1.0) * maxIdx;

                float rResult = mix(
                    lut.eval(float2(floor(rIdx), 0.0)).r,
                    lut.eval(float2(min(floor(rIdx) + 1.0, maxIdx), 0.0)).r,
                    fract(rIdx));
                float gResult = mix(
                    lut.eval(float2(floor(gIdx), 0.0)).g,
                    lut.eval(float2(min(floor(gIdx) + 1.0, maxIdx), 0.0)).g,
                    fract(gIdx));
                float bResult = mix(
                    lut.eval(float2(floor(bIdx), 0.0)).b,
                    lut.eval(float2(min(floor(bIdx) + 1.0, maxIdx), 0.0)).b,
                    fract(bIdx));

                float3 lutResult = srgbToLinear(float3(rResult, gResult, bResult));
                float3 result = mix(rgb, lutResult, strength);

                return half4(half3(result * alpha), half(alpha));
            }
            """;

        if (!SKSLShader.TryCreate(sksl1d, out s_1dShader, out string? error1d))
        {
            s_logger.LogError("Failed to compile 1D LUT SKSL: {ErrorText}", error1d);
        }
    }

    public LutEffect()
    {
        ScanProperties<LutEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CubeSource?> Source { get; } = Property.Create<CubeSource?>();

    [Display(Name = nameof(GraphicsStrings.Strength), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var cube = r.Source?.Cube;
        if (cube != null)
        {
            float strength = r.Strength / 100f;

            if (cube.Dimention == CubeFileDimension.OneDimension)
            {
                context.CustomEffect((cube, strength), OnApply1DLUT_GPU, static (_, r) => r);
            }
            else
            {
                context.CustomEffect((cube, strength), OnApply3DLUT_GPU, static (_, r) => r);
            }
        }
    }

    private void OnApply1DLUT_GPU((CubeFile cube, float strength) data, CustomFilterEffectContext c)
    {
        if (s_1dShader is null) return;

        for (int i = 0; i < c.Targets.Count; i++)
        {
            using var effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = image.ToShader();

            var builder = s_1dShader.CreateBuilder();

            using var lutImage = SKImage.Create(new SKImageInfo(data.cube.Data.Length, 1, SKColorType.RgbaF32));
            using (var pixmap = lutImage.PeekPixels())
            {
                var span = pixmap.GetPixelSpan<Vector4>();
                for (int j = 0; j < data.cube.Data.Length; j++)
                {
                    var color = data.cube.Data[j];
                    span[j] = new Vector4(color, 1);
                }
            }
            using var lutShader = lutImage.ToShader();

            builder.Children["src"] = baseShader;
            builder.Children["lut"] = lutShader;
            builder.Uniforms["lutSize"] = data.cube.Size;
            builder.Uniforms["strength"] = data.strength;

            c.Targets[i] = s_1dShader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
        }
    }

    private void OnApply3DLUT_GPU((CubeFile cube, float strength) data, CustomFilterEffectContext c)
    {
        if (s_shader is null) return;

        for (int i = 0; i < c.Targets.Count; i++)
        {
            using var effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = image.ToShader();

            var builder = s_shader.CreateBuilder();

            using var lutImage = SKImage.Create(new SKImageInfo(data.cube.Data.Length, 1, SKColorType.RgbaF32));
            using (var pixmap = lutImage.PeekPixels())
            {
                var span = pixmap.GetPixelSpan<Vector4>();
                for (int j = 0; j < data.cube.Data.Length; j++)
                {
                    var color = data.cube.Data[j];
                    span[j] = new Vector4(color, 1);
                }
            }
            using var lutShader = lutImage.ToShader();

            builder.Children["src"] = baseShader;
            builder.Children["lut"] = lutShader;
            builder.Uniforms["lutSize"] = data.cube.Size;
            builder.Uniforms["strength"] = data.strength;

            // 新しいターゲットに適用
            c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
        }
    }
}
