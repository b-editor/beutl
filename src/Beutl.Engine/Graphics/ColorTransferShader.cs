using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics;

/// <summary>Samples an image across the sRGB/linear-sRGB boundary between composition and effect buffers.</summary>
internal static class ColorTransferShader
{
    private static readonly Lazy<SKRuntimeEffect> s_effect = new(() =>
        SKRuntimeEffect.CreateShader("""
            uniform shader image;
            uniform shader converted;
            uniform int decode;
            uniform int exact;
            half4 main(float2 p) {
                float4 c = float4(image.eval(p));
                if (exact == 0 && c.a > 0.0) return converted.eval(p);
                // Channel-shift and additive effects can intentionally leave RGB at zero alpha, so
                // zero-alpha RGB is transferred as stored rather than unpremultiplied.
                float3 rgb = c.a > 0.0 ? c.rgb / c.a : c.rgb;
                float3 magnitude = abs(rgb);
                if (decode != 0) {
                    magnitude = mix(magnitude / 12.92,
                        pow((magnitude + 0.055) / 1.055, float3(2.4)),
                        step(float3(0.04045), magnitude));
                } else {
                    magnitude = mix(magnitude * 12.92,
                        1.055 * pow(magnitude, float3(1.0 / 2.4)) - 0.055,
                        step(float3(0.0031308), magnitude));
                }
                rgb = sign(rgb) * magnitude;
                return half4(half3(c.a > 0.0 ? rgb * c.a : rgb), half(c.a));
            }
            """, out string error) ?? throw new InvalidOperationException(error));

    /// <summary>
    /// Creates a shader that samples <paramref name="image"/> into <paramref name="destination"/>, or
    /// <see langword="null"/> when Skia's own conversion already preserves every pixel.
    /// </summary>
    public static SKShader? TryCreate(
        SKImage image,
        BitmapColorSpace destination,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKSamplingOptions sampling,
        SKMatrix localMatrix)
    {
        SKColorSpace? sourceSpace = image.ColorSpace;
        bool decode = destination == BitmapColorSpace.LinearSrgb
                      && sourceSpace?.IsSrgb == true;
        bool encode = destination == BitmapColorSpace.Srgb
                      && sourceSpace is not null
                      && SKColorSpace.Equal(sourceSpace, BitmapColorSpace.LinearSrgb.SKColorSpace);
        if (!decode && !encode)
            return null;

        // A premultiplied image sampled without cubic filtering is transferred exactly here, so a fused
        // shader run and its unfused stages decode identical values; Skia's conversion approximates the
        // curve. Otherwise Skia converts ordinary pixels and the raw child only answers for zero alpha,
        // where bilinear sampling stands in for the cubic filter Skia's raw shaders lack.
        bool exact = !sampling.UseCubic && image.AlphaType is SKAlphaType.Premul or SKAlphaType.Opaque;
        SKSamplingOptions rawSampling = sampling.UseCubic
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)
            : sampling;
        using SKShader raw = image.ToRawShader(tileModeX, tileModeY, rawSampling, localMatrix)
                             ?? throw new InvalidOperationException("The raw image shader could not be created.");
        using SKShader converted = image.ToShader(tileModeX, tileModeY, sampling, localMatrix);
        var builder = new SKRuntimeShaderBuilder(s_effect.Value);
        builder.Children["image"] = raw;
        builder.Children["converted"] = converted;
        builder.Uniforms["decode"] = decode ? 1 : 0;
        builder.Uniforms["exact"] = exact ? 1 : 0;
        return builder.Build();
    }

    /// <summary>Creates an image shader whose samples arrive in <paramref name="destination"/>.</summary>
    public static SKShader Create(
        SKImage image,
        BitmapColorSpace? destination,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKSamplingOptions sampling,
        SKMatrix localMatrix)
        => (destination is null ? null : TryCreate(image, destination, tileModeX, tileModeY, sampling, localMatrix))
           ?? image.ToShader(tileModeX, tileModeY, sampling, localMatrix);
}
