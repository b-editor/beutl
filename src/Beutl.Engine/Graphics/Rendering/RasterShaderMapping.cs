using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal static class RasterShaderMapping
{
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
            semanticSubset = PixelRect.FromRect(
                sourceBounds.Translate(new Vector(
                    -sourceRasterBounds.X,
                    -sourceRasterBounds.Y)),
                sourceScale);
            semanticRasterBounds = semanticSubset
                .ToRect(sourceScale)
                .Translate(new Vector(sourceRasterBounds.X, sourceRasterBounds.Y));
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
