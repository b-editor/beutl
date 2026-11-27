using System.Numerics;
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
        (PixelRect semanticDeviceBounds, PixelRect semanticSubset, Rect semanticRasterBounds) = ResolveSemanticSubset(
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

        using SKImage? subset = SubsetImage(image, recordingContext, semanticSubset);
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

    // Source texels per stage pixel; without a source grid the stage's own grid is used instead.
    public static void BindSourceRatio(ShaderUniformWriter writer, Vector2 unit, ShaderExecutionContext context)
        => writer.Set(context.InputRasterOrigin is null
            ? unit
            : unit * (context.InputEffectiveScale.Value / context.OutputPlacement.Scale.Value));

    // The source texel coordinate of stage pixel 0.
    public static void BindSourceOffset(ShaderUniformWriter writer, Vector2 none, ShaderExecutionContext context)
    {
        if (context.InputRasterOrigin is not { } inputOrigin)
        {
            writer.Set(none);
            return;
        }

        float scale = context.InputEffectiveScale.Value;
        Point logicalOrigin = context.OutputPlacement.LogicalOrigin;
        writer.Set(new Vector2((logicalOrigin.X - inputOrigin.X) * scale, (logicalOrigin.Y - inputOrigin.Y) * scale));
    }

    // A local logical point in stage pixels, anchored to the complete output also when only part of it is requested.
    public static Vector2 ToStagePixels(ShaderExecutionContext context, Point logical)
    {
        Point offset = logical - context.OutputPlacement.LogicalOrigin;
        return new Vector2(offset.X, offset.Y) * context.OutputPlacement.Scale.Value;
    }

    // The semantic device bounds, the same pixels inside an image of sourceDeviceBounds, and their raster bounds.
    public static (PixelRect DeviceBounds, PixelRect Subset, Rect RasterBounds) ResolveSemanticSubset(
        Rect sourceBounds,
        float sourceScale,
        PixelRect sourceDeviceBounds,
        Rect sourceRasterBounds)
    {
        (PixelRect deviceBounds, Rect rasterBounds) = ResolveSemanticSource(
            sourceBounds,
            sourceScale,
            sourceDeviceBounds,
            sourceRasterBounds);
        var subset = new PixelRect(
            deviceBounds.X - sourceDeviceBounds.X,
            deviceBounds.Y - sourceDeviceBounds.Y,
            deviceBounds.Width,
            deviceBounds.Height);
        return (deviceBounds, subset, rasterBounds);
    }

    public static SKImage? SubsetImage(SKImage image, GRRecordingContext? recordingContext, PixelRect subset)
        => recordingContext is null
            ? image.Subset(subset.ToSKRectI())
            : image.Subset(recordingContext, subset.ToSKRectI());

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
