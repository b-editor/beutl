using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.LUT_Cube_File), ResourceType = typeof(Strings))]
public sealed partial class LutEffect : FilterEffect
{
    private static readonly ILogger<LutEffect> s_logger =
        BeutlApplication.Current.LoggerFactory.CreateLogger<LutEffect>();

    private static readonly SKRuntimeEffect? s_runtimeEffect;

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

            float3 trilinear_interpolate(float4 color)
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

            float4 mix_strength(float3 color, float4 original) {
                float4 newColor;

                newColor.r = color.r * strength + (original.r * (1.0 - strength));
                newColor.g = color.g * strength + (original.g * (1.0 - strength));
                newColor.b = color.b * strength + (original.b * (1.0 - strength));
                newColor.a = original.a;

                return newColor;
            }

            half4 main(float2 fragCoord) {
                // 入力画像から色を取得
                float4 c = float4(src.eval(fragCoord));

                float3 newColor = trilinear_interpolate(c);

                return half4(mix_strength(newColor, c));
            }
            """;

        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public LutEffect()
    {
        ScanProperties<LutEffect>();
    }

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
    public IProperty<CubeSource?> Source { get; } = Property.Create<CubeSource?>();

    [Display(Name = nameof(Strings.Strength), ResourceType = typeof(Strings))]
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
                context.LookupTable(
                    cube,
                    strength,
                    static (CubeFile cube, (byte[] A, byte[] R, byte[] G, byte[] B) data) =>
                    {
                        LookupTable.Linear(data.A);
                        cube.ToLUT(1, data.R, data.G, data.B);
                    });
            }
            else
            {
                context.CustomEffect((cube, strength), OnApply3DLUT_GPU, static (_, r) => r);
            }
        }
    }

    private void OnApply3DLUT_GPU((CubeFile cube, float strength) data, CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(image);

            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

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
            using var lutShader = SKShader.CreateImage(lutImage);

            builder.Children["src"] = baseShader;
            builder.Children["lut"] = lutShader;
            builder.Uniforms["lutSize"] = data.cube.Size;
            builder.Uniforms["strength"] = data.strength;

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
