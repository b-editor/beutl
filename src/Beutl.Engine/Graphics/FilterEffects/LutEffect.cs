using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.CompilerServices;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LutEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LutEffect : FilterEffect
{
    // One snapshot per parsed file: nothing writes CubeFile.Data after parsing.
    private static readonly ConditionalWeakTable<CubeFile, LutShaderResource> s_lutSnapshots = new();

    private const string LutSamplingSource =
        """
            uniform shader lut;
            uniform int lutWidth;
            uniform float3 domainMin;
            uniform float3 domainMax;

            int modInt(int a, int b) {
                return a - b * (a / b);
            }

            float3 sampleLut(int index) {
                int x = modInt(index, lutWidth);
                int y = index / lutWidth;
                return float3(lut.eval(float2(float(x) + 0.5, float(y) + 0.5)).rgb);
            }


            """;

    private const string ShaderSource3D =
        LutSamplingSource
        + """


            uniform int lutSize;
            uniform float strength;

            float3 trilinear_interpolate(float3 inputColor)
            {
                int lutSize2 = lutSize * lutSize;
                float3 boundedColor = clamp(inputColor, float3(0.0), float3(1.0));
                float3 lutPosition = (boundedColor * 255.0) * float(lutSize) / 256.0;
                int posX = int(lutPosition.r);
                int posY = int(lutPosition.g);
                int posZ = int(lutPosition.b);

                float deltaX = lutPosition.r - float(posX);
                float deltaY = lutPosition.g - float(posY);
                float deltaZ = lutPosition.b - float(posZ);

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


            """
        + SkslSnippets.SrgbTransfer
        + """


            half4 apply(half4 color) {
                float4 c = float4(color);
                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 srgbColor = (linearToSrgb(rgb) - domainMin) / (domainMax - domainMin);
                float3 lutResult = trilinear_interpolate(srgbColor);
                lutResult = srgbToLinear(lutResult);
                float3 result = mix(rgb, lutResult, strength);

                const float HALF_MAX = 65504.0;
                float3 boundedResult = clamp(result * alpha, float3(-HALF_MAX), float3(HALF_MAX));
                return half4(half3(boundedResult), half(alpha));
            }
            """;

    private const string ShaderSource1D =
        LutSamplingSource
        + """


            uniform int lutSize;
            uniform float strength;


            """
        + SkslSnippets.SrgbTransfer
        + """


            half4 apply(half4 color) {
                float4 c = float4(color);

                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);
                float3 rgb = c.rgb / alpha;

                float3 srgbColor = (linearToSrgb(rgb) - domainMin) / (domainMax - domainMin);

                float maxIdx = float(lutSize - 1);
                float rIdx = clamp(srgbColor.r, 0.0, 1.0) * maxIdx;
                float gIdx = clamp(srgbColor.g, 0.0, 1.0) * maxIdx;
                float bIdx = clamp(srgbColor.b, 0.0, 1.0) * maxIdx;

                float rResult = mix(
                    sampleLut(int(floor(rIdx))).r,
                    sampleLut(int(min(floor(rIdx) + 1.0, maxIdx))).r,
                    fract(rIdx));
                float gResult = mix(
                    sampleLut(int(floor(gIdx))).g,
                    sampleLut(int(min(floor(gIdx) + 1.0, maxIdx))).g,
                    fract(gIdx));
                float bResult = mix(
                    sampleLut(int(floor(bIdx))).b,
                    sampleLut(int(min(floor(bIdx) + 1.0, maxIdx))).b,
                    fract(bIdx));

                float3 lutResult = srgbToLinear(float3(rResult, gResult, bResult));
                float3 result = mix(rgb, lutResult, strength);

                const float HALF_MAX = 65504.0;
                float3 boundedResult = clamp(result * alpha, float3(-HALF_MAX), float3(HALF_MAX));
                return half4(half3(boundedResult), half(alpha));
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
    public IExpressionProperty<CubeSource?> Source { get; } = Property.CreateExpression<CubeSource?>();

    [Display(Name = nameof(GraphicsStrings.Strength), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        CubeSource.Resource? source = r.Source;
        CubeFile? cube = source?.Cube;
        if (cube is null)
            return;

        LutShaderResource lutSnapshot = s_lutSnapshots.GetValue(cube, static c => LutShaderResource.Create(c.Data));
        RenderResource<LutShaderResource> lut = context.Borrow(lutSnapshot);
        // Non-finite or non-growing domains use the default 0 to 1.
        Vector3 domainSize = cube.Max - cube.Min;
        bool validDomain = domainSize.X > 0 && domainSize.Y > 0 && domainSize.Z > 0
            && float.IsFinite(domainSize.X) && float.IsFinite(domainSize.Y) && float.IsFinite(domainSize.Z);
        SkslSource shaderSource = cube.Dimention == CubeFileDimension.OneDimension
            ? s_shaderSource1D
            : s_shaderSource3D;

        context.Shader(ShaderDescription.CurrentPixel(
            shaderSource,
            bindings =>
            {
                bindings.Uniform("lutSize", cube.Size);
                bindings.Uniform("lutWidth", lutSnapshot.Width);
                bindings.Uniform("domainMin", validDomain ? cube.Min : Vector3.Zero);
                bindings.Uniform("domainMax", validDomain ? cube.Max : Vector3.One);
                bindings.Uniform("strength", r.Strength / 100f);
                bindings.Resource(
                    "lut",
                    lut,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, value, _) => writer.Set(value.CreateShader()));
            }));
    }

    private sealed class LutShaderResource
    {
        private readonly Vector3[] _data;
        private readonly int _height;

        private LutShaderResource(Vector3[] data)
        {
            _data = data;
            // A single row for a 33-point 3D LUT is 35,937 pixels wide, past common GPU limits.
            Width = (int)Math.Ceiling(Math.Sqrt(data.Length));
            _height = (data.Length + Width - 1) / Width;
        }

        public int Width { get; }

        public static LutShaderResource Create(ReadOnlySpan<Vector3> data)
            => new(data.ToArray());

        public SKShader CreateShader()
        {
            using SKColorSpace colorSpace = SKColorSpace.CreateSrgbLinear();
            using SKImage image = SKImage.Create(
                new SKImageInfo(Width, _height, SKColorType.RgbaF32, SKAlphaType.Premul, colorSpace));
            using (SKPixmap pixmap = image.PeekPixels())
            {
                Span<Vector4> pixels = pixmap.GetPixelSpan<Vector4>();
                pixels[_data.Length..].Clear();
                for (int i = 0; i < _data.Length; i++)
                {
                    pixels[i] = new Vector4(_data[i], 1);
                }
            }

            return image.ToShader();
        }
    }
}
