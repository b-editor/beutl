using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Shaders;

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
            deviceBounds.ToRect(workingScale).Translate(-deviceGridOffset));
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
        Rect canonicalRasterBounds = sourceDeviceBounds.ToRect(sourceScale);
        (PixelRect semanticDeviceBounds, Rect semanticRasterBounds) = ResolveSemanticSource(
            sourceBounds,
            sourceScale,
            sourceDeviceBounds,
            sourceRasterBounds);
        if (sourceRasterBounds == canonicalRasterBounds && !sourceDeviceBounds.Contains(semanticDeviceBounds))
        {
            throw new ArgumentException(
                "The source device bounds must contain the complete semantic source bounds.",
                nameof(sourceDeviceBounds));
        }

        var semanticSubset = new PixelRect(
            semanticDeviceBounds.X - sourceDeviceBounds.X,
            semanticDeviceBounds.Y - sourceDeviceBounds.Y,
            semanticDeviceBounds.Width,
            semanticDeviceBounds.Height);

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

    // Where the texel grid a semantic image shader samples starts, in the source's local logical coordinates.
    public static Point SemanticRasterOrigin(
        Rect sourceBounds,
        float sourceScale,
        PixelRect sourceDeviceBounds,
        Rect sourceRasterBounds)
        => ResolveSemanticSource(sourceBounds, sourceScale, sourceDeviceBounds, sourceRasterBounds)
            .RasterBounds.Position;

    private static (PixelRect DeviceBounds, Rect RasterBounds) ResolveSemanticSource(
        Rect sourceBounds,
        float sourceScale,
        PixelRect sourceDeviceBounds,
        Rect sourceRasterBounds)
    {
        // Round the semantic extent on the shared device grid. Subtracting raster-local
        // floating-point origins first can move an exact edge across a pixel boundary and
        // include a transparent apron in the subset that Clamp treats as the source edge.
        Vector deviceGridOffset = sourceDeviceBounds.ToRect(sourceScale).Position - sourceRasterBounds.Position;
        PixelRect semanticDeviceBounds = PixelRect.FromRect(
            sourceBounds.Translate(deviceGridOffset),
            sourceScale);
        return (semanticDeviceBounds, semanticDeviceBounds.ToRect(sourceScale).Translate(-deviceGridOffset));
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
