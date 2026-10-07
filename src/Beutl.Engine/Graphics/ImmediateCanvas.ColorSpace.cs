using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    private static readonly Lazy<SKRuntimeEffect> s_imageTransferEffect = new(() =>
        SKRuntimeEffect.CreateShader("""
            uniform shader image;
            uniform shader converted;
            uniform int decode;
            half4 main(float2 p) {
                half4 c = image.eval(p);
                if (c.a > 0.0) return converted.eval(p);
                // Channel-shift and additive effects can intentionally leave RGB at zero alpha.
                // At zero alpha there is no unpremultiplication; ordinary pixels use Skia above.
                float3 rgb = float3(c.rgb);
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
                return half4(half3(sign(rgb) * magnitude), c.a);
            }
            """, out string error) ?? throw new InvalidOperationException(error));

    private void DrawTransferredImage(SKImage image, SKRect source, SKRect destination, SKSamplingOptions sampling)
    {
        SKColorSpace? sourceSpace = image.ColorSpace;
        BitmapColorSpace destinationSpace = WorkingColorSpace;
        bool decode = destinationSpace == BitmapColorSpace.LinearSrgb
                      && sourceSpace?.IsSrgb == true;
        bool encode = destinationSpace == BitmapColorSpace.Srgb
                      && sourceSpace is not null
                      && SKColorSpace.Equal(sourceSpace, BitmapColorSpace.LinearSrgb.SKColorSpace);
        if (!decode && !encode)
        {
            Canvas.DrawImage(image, source, destination, sampling, _sharedFillPaint);
            return;
        }

        float sx = destination.Width / source.Width;
        float sy = destination.Height / source.Height;
        var matrix = SKMatrix.CreateScaleTranslation(sx, sy,
            destination.Left - source.Left * sx, destination.Top - source.Top * sy);
        // The destination rectangle supplies edge coverage, just like DrawImage. Decal sampling
        // would fade at the texture border as well and apply AA twice to a rotated opaque image.
        using SKShader raw = image.ToRawShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, matrix);
        using SKShader converted = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, matrix);
        var builder = new SKRuntimeShaderBuilder(s_imageTransferEffect.Value);
        builder.Children["image"] = raw;
        builder.Children["converted"] = converted;
        builder.Uniforms["decode"] = decode ? 1 : 0;
        using SKShader shader = builder.Build();
        _sharedFillPaint.Shader = shader;
        try
        {
            Canvas.DrawRect(destination, _sharedFillPaint);
        }
        finally
        {
            _sharedFillPaint.Shader = null;
        }
    }
}
