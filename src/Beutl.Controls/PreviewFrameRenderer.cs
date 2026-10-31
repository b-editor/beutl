using Avalonia.Media.Imaging;
using Beutl.Configuration;
using SkiaSharp;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.Controls;

// Shared SDR preview conversion for the editor and remote displays. Draw onto an untagged
// 8-bit surface: the shader already produces sRGB values.
internal static class PreviewFrameRenderer
{
    private static readonly SKPaint s_linearPaint =
        BitmapView.CreatePreviewPaint(SKColorFilter.CreateLinearToSrgbGamma());

    private static readonly SKPaint s_gammaPaint = BitmapView.CreatePreviewPaint(null);

    private static readonly SKRuntimeEffect? s_toneMappingEffect;

    static PreviewFrameRenderer()
    {
        string sksl = """
            uniform shader src;
            uniform float exposure;
            uniform int tmOperator;

            float3 linearToSrgb(float3 c) {
                float3 lo = c * 12.92;
                float3 hi = 1.055 * pow(max(c, float3(0.0)), float3(1.0/2.4)) - 0.055;
                return mix(lo, hi, step(float3(0.0031308), c));
            }

            float3 reinhard(float3 c) {
                return c / (1.0 + c);
            }

            float3 aces(float3 c) {
                const float a = 2.51;
                const float b = 0.03;
                const float cc = 2.43;
                const float d = 0.59;
                const float e = 0.14;
                return clamp((c * (a * c + b)) / (c * (cc * c + d) + e), 0.0, 1.0);
            }

            float3 hable_partial(float3 x) {
                const float A = 0.15;
                const float B = 0.50;
                const float C = 0.10;
                const float D = 0.20;
                const float E = 0.02;
                const float F = 0.30;
                return ((x * (A*x + C*B) + D*E) / (x * (A*x + B) + D*F)) - E/F;
            }

            float3 hable(float3 c) {
                float3 W = float3(11.2);
                return hable_partial(c) / hable_partial(W);
            }

            half4 main(float2 coord) {
                half4 c = src.eval(coord);
                float alpha = c.a;
                if (alpha <= 0.0001) return half4(0.0);

                float3 rgb = c.rgb / alpha;
                rgb *= exp2(exposure);

                if (tmOperator == 1) {
                    rgb = reinhard(max(rgb, float3(0.0)));
                } else if (tmOperator == 2) {
                    rgb = aces(max(rgb, float3(0.0)));
                } else if (tmOperator == 3) {
                    rgb = hable(max(rgb, float3(0.0)));
                }

                rgb = clamp(rgb, 0.0, 1.0);
                rgb = linearToSrgb(rgb);

                return half4(half3(rgb * alpha), half(alpha));
            }
            """;

        s_toneMappingEffect = SKRuntimeEffect.CreateShader(sksl, out _);
    }

    internal static void Draw(SKCanvas canvas, BtlBitmap bitmap, SKRect sourceRect, SKRect destRect,
        BitmapInterpolationMode interpolationMode, UIToneMappingOperator tmOperator, float tmExposure)
    {
        using var image = SKImage.FromBitmap(bitmap.SKBitmap);
        Draw(canvas, image, sourceRect, destRect, interpolationMode, tmOperator, tmExposure);
    }

    // A raster canvas reads the image's pixels during the draw call, so its image may wrap them instead
    // of copying. A GPU canvas can upload them later and draws through the bitmap overload.
    internal static void Draw(SKCanvas canvas, SKImage? image, SKRect sourceRect, SKRect destRect,
        BitmapInterpolationMode interpolationMode, UIToneMappingOperator tmOperator, float tmExposure)
    {
        if (image == null)
            return;

        var sampling = interpolationMode switch
        {
            BitmapInterpolationMode.None => SKSamplingOptions.Default,
            BitmapInterpolationMode.LowQuality => new SKSamplingOptions(SKFilterMode.Linear),
            BitmapInterpolationMode.MediumQuality =>
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            BitmapInterpolationMode.HighQuality => new SKSamplingOptions(SKCubicResampler.Mitchell),
            _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
        };

        bool isLinear = image.ColorSpace?.GammaIsLinear == true;
        bool needsToneMapping = isLinear && tmOperator != UIToneMappingOperator.None && s_toneMappingEffect != null;

        if (needsToneMapping)
        {
            // Cubic image sampling clamps RGB to alpha before the tone mapper can see HDR values.
            if (sampling.UseCubic)
                sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            float scaleX = sourceRect.Width / destRect.Width;
            float scaleY = sourceRect.Height / destRect.Height;
            float transX = sourceRect.Left - destRect.Left * scaleX;
            float transY = sourceRect.Top - destRect.Top * scaleY;
            var localMatrix = SKMatrix.CreateScaleTranslation(1 / scaleX, 1 / scaleY, transX, transY);

            using var imageShader = image.ToShader(
                SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                sampling, localMatrix);
            var builder = new SKRuntimeShaderBuilder(s_toneMappingEffect!);
            builder.Children["src"] = imageShader;
            builder.Uniforms["exposure"] = tmExposure;
            builder.Uniforms["tmOperator"] = (int)tmOperator;

            using var finalShader = builder.Build();
            using var paint = BitmapView.CreatePreviewPaint(null);
            paint.Shader = finalShader;
            canvas.DrawRect(destRect, paint);
        }
        else
        {
            canvas.DrawImage(image, sourceRect, destRect, sampling,
                isLinear ? s_linearPaint : s_gammaPaint);
        }
    }
}
