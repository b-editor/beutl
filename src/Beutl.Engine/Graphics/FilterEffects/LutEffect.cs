using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LutEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LutEffect : FilterEffect
{
    private static readonly ConditionalWeakTable<CubeFile, LutSnapshotState> s_lutSnapshots = new();

    private const string ShaderSource3D =
        """
            uniform shader lut;
            uniform int lutSize;
            uniform float strength;

            int modInt(int a, int b) {
                return a - b * (a / b);
            }

            float3 sampleLut(int index) {
                return float3(lut.eval(float2(float(index) + 0.5, 0.5)).rgb);
            }

            float3 trilinear_interpolate(float3 inputColor)
            {
                int lutSize2 = lutSize * lutSize;
                int posX = int(clamp((inputColor.r * 255.0) * float(lutSize) / 256.0, 0, 255));
                int posY = int(clamp((inputColor.g * 255.0) * float(lutSize) / 256.0, 0, 255));
                int posZ = int(clamp((inputColor.b * 255.0) * float(lutSize) / 256.0, 0, 255));

                float deltaX = ((inputColor.r * 255.0) * float(lutSize) / 256.0) - float(posX);
                float deltaY = ((inputColor.g * 255.0) * float(lutSize) / 256.0) - float(posY);
                float deltaZ = ((inputColor.b * 255.0) * float(lutSize) / 256.0) - float(posZ);

                int index = posX + posY * lutSize + posZ * lutSize2;
                int nextIndex0 = 1;
                int nextIndex1 = lutSize;
                int nextIndex2 = lutSize2;

                if (modInt(index, lutSize) == lutSize - 1)
                {
                    nextIndex0 = 0;
                }
                if (modInt(index / lutSize, lutSize) == lutSize - 1)
                {
                    nextIndex1 = 0;
                }
                if (modInt(index / lutSize2, lutSize) == lutSize - 1)
                {
                    nextIndex2 = 0;
                }

                float3 vertexColor0 = sampleLut(index);
                float3 vertexColor1 = sampleLut(index + nextIndex0);
                float3 vertexColor2 = sampleLut(index + nextIndex0 + nextIndex1);
                float3 vertexColor3 = sampleLut(index + nextIndex1);
                float3 vertexColor4 = sampleLut(index + nextIndex2);
                float3 vertexColor5 = sampleLut(index + nextIndex0 + nextIndex2);
                float3 vertexColor6 = sampleLut(index + nextIndex0 + nextIndex1 + nextIndex2);
                float3 vertexColor7 = sampleLut(index + nextIndex1 + nextIndex2);

                float3 surfaceColor0 = vertexColor0 * (1.0 - deltaZ) + vertexColor4 * deltaZ;
                float3 surfaceColor1 = vertexColor1 * (1.0 - deltaZ) + vertexColor5 * deltaZ;
                float3 surfaceColor2 = vertexColor2 * (1.0 - deltaZ) + vertexColor6 * deltaZ;
                float3 surfaceColor3 = vertexColor3 * (1.0 - deltaZ) + vertexColor7 * deltaZ;

                float3 lineColor0 = surfaceColor0 * (1.0 - deltaX) + surfaceColor1 * deltaX;
                float3 lineColor1 = surfaceColor3 * (1.0 - deltaX) + surfaceColor2 * deltaX;
                float3 outputColor = lineColor0 * (1.0 - deltaY) + lineColor1 * deltaY;

                return outputColor;
            }

            float3 linearToSrgb(float3 c) {
                float3 lo = c * 12.92;
                float3 hi = 1.055 * pow(c, float3(1.0/2.4)) - 0.055;
                return mix(lo, hi, step(float3(0.0031308), c));
            }

            float3 srgbToLinear(float3 c) {
                float3 lo = c / 12.92;
                float3 hi = pow((c + 0.055) / 1.055, float3(2.4));
                return mix(lo, hi, step(float3(0.04045), c));
            }

            half4 apply(half4 color) {
                float4 c = float4(color);
                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 srgbColor = linearToSrgb(rgb);
                float3 lutResult = trilinear_interpolate(srgbColor);
                lutResult = srgbToLinear(lutResult);
                float3 result = mix(rgb, lutResult, strength);

                return half4(half3(result * alpha), half(alpha));
            }
            """;

    private const string ShaderSource1D =
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

            half4 apply(half4 color) {
                float4 c = float4(color);

                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 srgbColor = linearToSrgb(rgb);

                float maxIdx = float(lutSize - 1);
                float rIdx = clamp(srgbColor.r, 0.0, 1.0) * maxIdx;
                float gIdx = clamp(srgbColor.g, 0.0, 1.0) * maxIdx;
                float bIdx = clamp(srgbColor.b, 0.0, 1.0) * maxIdx;

                float rResult = mix(
                    lut.eval(float2(floor(rIdx) + 0.5, 0.5)).r,
                    lut.eval(float2(min(floor(rIdx) + 1.0, maxIdx) + 0.5, 0.5)).r,
                    fract(rIdx));
                float gResult = mix(
                    lut.eval(float2(floor(gIdx) + 0.5, 0.5)).g,
                    lut.eval(float2(min(floor(gIdx) + 1.0, maxIdx) + 0.5, 0.5)).g,
                    fract(gIdx));
                float bResult = mix(
                    lut.eval(float2(floor(bIdx) + 0.5, 0.5)).b,
                    lut.eval(float2(min(floor(bIdx) + 1.0, maxIdx) + 0.5, 0.5)).b,
                    fract(bIdx));

                float3 lutResult = srgbToLinear(float3(rResult, gResult, bResult));
                float3 result = mix(rgb, lutResult, strength);

                return half4(half3(result * alpha), half(alpha));
            }
            """;

    private static readonly SkslSource s_shaderSource3D =
        new(ShaderSource3D, ShaderDescriptionKind.CurrentPixel);

    private static readonly SkslSource s_shaderSource1D =
        new(ShaderSource1D, ShaderDescriptionKind.CurrentPixel);

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
        CubeSource.Resource? source = r.Source;
        CubeFile? cube = source?.Cube;
        if (source is null || cube is null)
            return;

        LutShaderResource lutSnapshot = s_lutSnapshots
            .GetValue(cube, static _ => new LutSnapshotState())
            .GetOrCreate(cube.Data);
        RenderResource<LutShaderResource> lut = context.Borrow(
            lutSnapshot,
            lutSnapshot.ContentIdentity);
        SkslSource shaderSource = cube.Dimention == CubeFileDimension.OneDimension
            ? s_shaderSource1D
            : s_shaderSource3D;

        context.Shader(ShaderDescription.CurrentPixel(
            shaderSource,
            bindings =>
            {
                bindings.Uniform("lutSize", cube.Size);
                bindings.Uniform("strength", r.Strength / 100f);
                bindings.Resource(
                    "lut",
                    lut,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, value, _) => writer.Set(value.CreateShader()),
                    cachePolicy: ShaderBindingCachePolicy.ReuseFromSnapshot);
            }));
    }

    private sealed class LutSnapshotState
    {
        private readonly object _gate = new();
        private LutShaderResource? _current;

        public LutShaderResource GetOrCreate(ReadOnlySpan<Vector3> data)
        {
            lock (_gate)
            {
                if (_current is not null && _current.HasSameContent(data))
                    return _current;

                _current = LutShaderResource.Create(data);
                return _current;
            }
        }
    }

    private sealed class LutShaderResource
    {
        private readonly Vector3[] _data;

        private LutShaderResource(Vector3[] data)
        {
            _data = data;
            ContentIdentity = new LutContentIdentity();
        }

        public LutContentIdentity ContentIdentity { get; }

        public static LutShaderResource Create(ReadOnlySpan<Vector3> data)
            => new(data.ToArray());

        public bool HasSameContent(ReadOnlySpan<Vector3> data)
            => MemoryMarshal.AsBytes(_data.AsSpan())
                .SequenceEqual(MemoryMarshal.AsBytes(data));

        public SKShader CreateShader()
        {
            using SKImage image = SKImage.Create(
                new SKImageInfo(_data.Length, 1, SKColorType.RgbaF32));
            using (SKPixmap pixmap = image.PeekPixels())
            {
                Span<Vector4> pixels = pixmap.GetPixelSpan<Vector4>();
                for (int i = 0; i < _data.Length; i++)
                {
                    pixels[i] = new Vector4(_data[i], 1);
                }
            }

            return image.ToShader();
        }
    }

    private sealed class LutContentIdentity
    {
    }
}
