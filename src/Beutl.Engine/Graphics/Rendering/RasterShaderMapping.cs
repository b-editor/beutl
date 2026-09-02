using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal static class RasterShaderMapping
{
    private static readonly SKSamplingOptions s_linearSampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);
    private static readonly SKSamplingOptions s_cubicSampling =
        new(SKCubicResampler.Mitchell);

    public static SKSamplingOptions SamplingFor(float sourceScale, float destinationScale)
        => destinationScale > sourceScale ? s_cubicSampling : s_linearSampling;

    /// <summary>
    /// Resolves the complete-output device frame a WholeSource stage is evaluated in, given the destination
    /// footprint the renderer actually allocated for the requested region.
    /// </summary>
    public static ShaderEvaluationFrame CreateWholeSourceFrame(
        Rect outputBounds,
        PixelRect destinationDeviceBounds,
        Rect destinationRasterBounds,
        float workingScale)
    {
        var deviceGridOffset = new Vector(
            (destinationDeviceBounds.X / workingScale) - destinationRasterBounds.X,
            (destinationDeviceBounds.Y / workingScale) - destinationRasterBounds.Y);
        PixelRect deviceBounds = PixelRect.FromRect(
            outputBounds.Translate(deviceGridOffset),
            workingScale);
        return new ShaderEvaluationFrame(
            deviceBounds,
            deviceBounds.ToRect(workingScale).Translate(-deviceGridOffset),
            destinationDeviceBounds.Position - deviceBounds.Position);
    }

    public static SKShader CreateSemanticImageShader(
        SKImage image,
        GRRecordingContext? recordingContext,
        Rect sourceBounds,
        float sourceScale,
        PixelRect sourceDeviceBounds,
        Rect sourceRasterBounds,
        float destinationScale,
        Rect destinationRasterBounds,
        SKShaderTileMode tileMode)
    {
        ArgumentNullException.ThrowIfNull(image);
        var imageBounds = new PixelRect(new PixelSize(image.Width, image.Height));
        PixelRect semanticSubset;
        Rect semanticRasterBounds;
        Rect canonicalRasterBounds = sourceDeviceBounds.ToRect(sourceScale);
        if (sourceRasterBounds == canonicalRasterBounds)
        {
            PixelRect semanticDeviceBounds = PixelRect.FromRect(sourceBounds, sourceScale);
            if (!sourceDeviceBounds.Contains(semanticDeviceBounds))
            {
                throw new ArgumentException(
                    "The source device bounds must contain the complete semantic source bounds.",
                    nameof(sourceDeviceBounds));
            }

            semanticSubset = new PixelRect(
                semanticDeviceBounds.X - sourceDeviceBounds.X,
                semanticDeviceBounds.Y - sourceDeviceBounds.Y,
                semanticDeviceBounds.Width,
                semanticDeviceBounds.Height);
            semanticRasterBounds = semanticDeviceBounds.ToRect(sourceScale);
        }
        else
        {
            // Round the semantic extent on the shared device grid. Subtracting raster-local
            // floating-point origins first can move an exact edge across a pixel boundary and
            // include a transparent apron in the subset that Clamp treats as the source edge.
            Vector deviceGridOffset = canonicalRasterBounds.Position - sourceRasterBounds.Position;
            PixelRect semanticDeviceBounds = PixelRect.FromRect(
                sourceBounds.Translate(deviceGridOffset),
                sourceScale);
            semanticSubset = new PixelRect(
                semanticDeviceBounds.X - sourceDeviceBounds.X,
                semanticDeviceBounds.Y - sourceDeviceBounds.Y,
                semanticDeviceBounds.Width,
                semanticDeviceBounds.Height);
            semanticRasterBounds = semanticDeviceBounds
                .ToRect(sourceScale)
                .Translate(-deviceGridOffset);
        }

        if (!imageBounds.Contains(semanticSubset))
        {
            throw new ArgumentException(
                "The source raster bounds must contain the complete semantic source bounds.",
                nameof(sourceRasterBounds));
        }

        SKMatrix localMatrix = CreateLocalMatrix(
            destinationScale,
            sourceScale,
            destinationRasterBounds,
            semanticRasterBounds);
        if (semanticSubset == imageBounds)
        {
            return image.ToShader(
                tileMode,
                tileMode,
                SKSamplingOptions.Default,
                localMatrix);
        }

        using SKImage subset = recordingContext is null
            ? image.Subset(semanticSubset.ToSKRectI())
            : image.Subset(recordingContext, semanticSubset.ToSKRectI());
        if (subset is null)
            throw new InvalidOperationException("The semantic shader source subset could not be created.");
        return subset.ToShader(
            tileMode,
            tileMode,
            SKSamplingOptions.Default,
            localMatrix);
    }

    public static SKMatrix CreateLocalMatrix(
        float destinationScale,
        float sourceScale,
        Rect destinationRasterBounds,
        Rect sourceRasterBounds)
    {
        float scale = destinationScale / sourceScale;
        float offsetX = (float)(
            -(destinationRasterBounds.X - sourceRasterBounds.X) * destinationScale);
        float offsetY = (float)(
            -(destinationRasterBounds.Y - sourceRasterBounds.Y) * destinationScale);
        return new SKMatrix(
            scale,
            0,
            offsetX,
            0,
            scale,
            offsetY,
            0,
            0,
            1);
    }
}
