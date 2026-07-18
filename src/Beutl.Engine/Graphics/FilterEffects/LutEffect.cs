using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LutEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LutEffect : FilterEffect
{
    // Fusable snippet forms of the two shaders above; `c` is the premultiplied linear-light source pixel
    // (contract A2). Each apply function un-premultiplies RGB, converts to sRGB for LUT lookup, converts the result
    // back to linear light, and re-premultiplies the result.
    // The LUT is an extra texture sampler (its contents are a parameter, A4).
    private static readonly string s_snippet3d =
        """
        uniform shader lut;
        uniform int lutSize;
        uniform float strength;

        int modInt(int a, int b) {
            return a - b * (a / b);
        }

        float3 trilinear_interpolate(float3 color)
        {
            int3 pos;
            float3 delta;
            int lutSize2 = lutSize * lutSize;

            pos.x = int(clamp((color.r * 255.0) * float(lutSize) / 256.0, 0, 255));
            pos.y = int(clamp((color.g * 255.0) * float(lutSize) / 256.0, 0, 255));
            pos.z = int(clamp((color.b * 255.0) * float(lutSize) / 256.0, 0, 255));

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

        half4 apply(half4 c) {
            float4 cc = float4(c);

            float alpha = cc.a;
            if (alpha <= 0.0001) return half4(0.0);
            float3 rgb = cc.rgb / alpha;

            float3 srgbColor = linearToSrgb(rgb);
            float3 lutResult = trilinear_interpolate(srgbColor);

            lutResult = srgbToLinear(lutResult);

            float3 result = mix(rgb, lutResult, strength);

            return half4(half3(result * alpha), half(alpha));
        }
        """;

    private static readonly string s_1dSnippet =
        """
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

        half4 apply(half4 c) {
            float4 cc = float4(c);

            float alpha = cc.a;
            if (alpha <= 0.0001) return half4(0.0);
            float3 rgb = cc.rgb / alpha;

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

    public LutEffect()
    {
        ScanProperties<LutEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CubeSource?> Source { get; } = Property.Create<CubeSource?>();

    [Display(Name = nameof(GraphicsStrings.Strength), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var cube = r.Source?.Cube;
        // No source cube renders identity today (ApplyTo appends nothing); describe nothing so the effect is a
        // true no-op node in the graph.
        if (cube == null)
            return;

        float strength = r.Strength / 100f;
        string snippet = cube.Dimention == CubeFileDimension.OneDimension ? s_1dSnippet : s_snippet3d;
        // The LUT texture is a pure function of the cube, so it is cached on the resource (rebuilt only when the
        // cube changes) and bound caller-owned — the resource owns the shader, so the graph must not dispose it.
        SKShader lutShader = r.GetOrBuildLutShader(cube);
        builder.Shader(ShaderNodeDescriptor.Snippet(
            snippet,
            u => u.Int("lutSize", cube.Size).Float("strength", strength),
            samplers: [new ChildBinding("lut", lutShader)]));
    }

    public new partial class Resource
    {
        private CubeFile? _cachedCube;
        private SKShader? _cachedLutShader;

        // A CubeFile is immutable and reference-stable while the source is unchanged (CubeSource weak-caches it),
        // so instance identity is a sound cache key: same cube -> same rasterized shader; a changed cube rebuilds.
        internal SKShader GetOrBuildLutShader(CubeFile cube)
        {
            if (_cachedLutShader is null || !ReferenceEquals(_cachedCube, cube))
            {
                // Build before swapping: a BuildLutShader throw must not leave the cache holding a disposed shader.
                SKShader shader = BuildLutShader(cube);
                _cachedLutShader?.Dispose();
                _cachedLutShader = shader;
                _cachedCube = cube;
            }

            return _cachedLutShader;
        }

        private bool ClearCachedLutShader()
        {
            bool hadCachedLut = _cachedLutShader is not null || _cachedCube is not null;
            SKShader? cachedLutShader = _cachedLutShader;
            _cachedLutShader = null;
            _cachedCube = null;
            cachedLutShader?.Dispose();
            return hadCachedLut;
        }

        partial void PostUpdate(LutEffect obj, CompositionContext context)
        {
            if (Source?.Cube == null && ClearCachedLutShader())
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                ClearCachedLutShader();
        }
    }

    // A one-row RgbaF32 texture of the cube entries. Mirrors CurveMap.ToShader: the shader keeps the native image
    // alive after the managed wrapper is disposed, so the shader is self-contained.
    private static SKShader BuildLutShader(CubeFile cube)
    {
        using var lutImage = SKImage.Create(new SKImageInfo(cube.Data.Length, 1, SKColorType.RgbaF32));
        using (var pixmap = lutImage.PeekPixels())
        {
            var span = pixmap.GetPixelSpan<Vector4>();
            for (int j = 0; j < cube.Data.Length; j++)
            {
                span[j] = new Vector4(cube.Data[j], 1);
            }
        }

        return lutImage.ToShader();
    }
}
