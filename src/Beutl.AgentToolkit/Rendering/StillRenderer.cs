using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderStillResponse(
    string OutputPath,
    int Width,
    int Height,
    string Time,
    IReadOnlyList<string> Warnings,
    StillFrameVisibilityAnalysis? VisibilityAnalysis = null,
    IReadOnlyList<RenderStillActiveElement>? ActiveElements = null);

public sealed record StillFrameVisibilityAnalysis(
    int TotalPixels,
    int VisiblePixels,
    double VisiblePixelRatio,
    int ForegroundPixels,
    double ForegroundPixelRatio,
    double OccupiedBoundsRatio,
    double MaxQuadrantForegroundRatio,
    int Left,
    int Top,
    int Right,
    int Bottom,
    int MinLuma,
    int MaxLuma,
    double MeanLuma,
    double LumaStandardDeviation,
    double BackgroundLuma,
    int VisibilityThreshold,
    int ForegroundDeltaThreshold,
    IReadOnlyList<string> Warnings);

public sealed record RenderStillActiveElement(
    string Id,
    string Name,
    string Start,
    string Length,
    int ZIndex,
    int ObjectCount);

public sealed record RenderStoryboardResponse(
    string ContactSheetPath,
    IReadOnlyList<RenderStoryboardShot> Shots);

public sealed record RenderStoryboardShot(
    string Name,
    double TimeSeconds,
    string StillPath,
    StillFrameVisibilityAnalysis? VisibilityAnalysis,
    string Kind = "shot",
    int SubdivisionLevel = 0);

public sealed record StoryboardShotInput(
    string Name,
    double TimeSeconds);

public sealed record RenderStoryboardResult(
    string Status,
    string? JobId,
    RenderStoryboardResponse? Result);

public sealed class StillRenderer
{
    public async ValueTask<RenderStillResponse> RenderAsync(
        Scene scene,
        TimeSpan time,
        string outputPath,
        float renderScale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        using Bitmap snapshot = await RenderBitmapAsync(
            scene,
            time,
            normalizedScale,
            cancellationToken).ConfigureAwait(false);

        if (!snapshot.Save(outputPath, EncodedImageFormat.Png))
        {
            throw new IOException($"Failed to write still image to '{outputPath}'.");
        }

        StillFrameVisibilityAnalysis analysis = AnalyzeFrameVisibility(snapshot);
        return new RenderStillResponse(
            outputPath,
            snapshot.Width,
            snapshot.Height,
            time.ToString("c"),
            analysis.Warnings,
            analysis,
            CreateActiveElementSummaries(scene, time));
    }

    public async ValueTask<Bitmap> RenderBitmapAsync(
        Scene scene,
        TimeSpan time,
        float renderScale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ContainsGpuOnlyContent(scene)
            && !await Has3DGraphicsContextAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new RenderingUnavailableException(
                "The scene contains 3D content, but no GPU context with 3D rendering support is available.");
        }

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            using var renderer = new SceneRenderer(scene, normalizedScale, disableResourceShare: true);
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            var frame = renderer.Compositor.EvaluateGraphics(time + scene.Start);
            renderer.Render(frame);
            return renderer.Snapshot();
        }, ct: cancellationToken).ConfigureAwait(false);
    }

    private static bool ContainsGpuOnlyContent(Scene scene)
    {
        return scene is IHierarchical hierarchical
               && hierarchical.EnumerateAllChildren<Scene3D>().Any();
    }

    private static async ValueTask<bool> Has3DGraphicsContextAsync(CancellationToken cancellationToken)
    {
        return await RenderThread.Dispatcher.InvokeAsync(
            () => GraphicsContextFactory.GetOrCreateShared()?.Supports3DRendering == true,
            ct: cancellationToken).ConfigureAwait(false);
    }

    private static StillFrameVisibilityAnalysis AnalyzeFrameVisibility(Bitmap bitmap)
    {
        const int nearBlackThreshold = 8;
        const int maxForegroundDeltaThreshold = 12;
        const double minimumVisibleRatio = 0.005;
        const double minimumContrastStdDev = 3;
        const int minimumContrastRange = 18;
        const double confinedBoundsRatio = 0.12;
        const double confinedQuadrantRatio = 0.90;

        int totalPixels = bitmap.Width * bitmap.Height;
        if (totalPixels == 0)
        {
            return EmptyAnalysis("Rendered frame has no pixels.", nearBlackThreshold, maxForegroundDeltaThreshold);
        }

        int colorByteCount = GetColorByteCount(bitmap.ColorType, bitmap.BytesPerPixel);
        if (colorByteCount <= 0)
        {
            return EmptyAnalysis(
                "Rendered frame uses a pixel format that could not be analyzed for visibility.",
                nearBlackThreshold,
                maxForegroundDeltaThreshold);
        }

        int visiblePixels = 0;
        int minLuma = byte.MaxValue;
        int maxLuma = byte.MinValue;
        double totalLuma = 0;
        double totalLumaSquared = 0;
        double borderLuma = 0;
        int borderPixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int pixelOffset = x * bitmap.BytesPerPixel;
                int maxColorByte = 0;
                int luma = 0;
                for (int channel = 0; channel < colorByteCount; channel++)
                {
                    byte value = row[pixelOffset + channel];
                    maxColorByte = Math.Max(maxColorByte, value);
                    luma += value;
                }

                luma /= colorByteCount;
                if (maxColorByte > nearBlackThreshold)
                {
                    visiblePixels++;
                }

                minLuma = Math.Min(minLuma, luma);
                maxLuma = Math.Max(maxLuma, luma);
                totalLuma += luma;
                totalLumaSquared += luma * luma;
                if (IsBorderPixel(bitmap.Width, bitmap.Height, x, y))
                {
                    borderLuma += luma;
                    borderPixels++;
                }
            }
        }

        double visibleRatio = visiblePixels / (double)totalPixels;
        double meanLuma = totalLuma / totalPixels;
        double variance = Math.Max(0, totalLumaSquared / totalPixels - meanLuma * meanLuma);
        double lumaStdDev = Math.Sqrt(variance);
        double backgroundLuma = borderPixels == 0 ? meanLuma : borderLuma / borderPixels;
        int lumaRange = maxLuma - minLuma;
        int foregroundDeltaThreshold = Math.Clamp(lumaRange / 3, 4, maxForegroundDeltaThreshold);
        int foregroundPixels = 0;
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = -1;
        int bottom = -1;
        Span<int> quadrants = stackalloc int[4];

        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int pixelOffset = x * bitmap.BytesPerPixel;
                int luma = 0;
                for (int channel = 0; channel < colorByteCount; channel++)
                {
                    luma += row[pixelOffset + channel];
                }

                luma /= colorByteCount;
                if (Math.Abs(luma - backgroundLuma) <= foregroundDeltaThreshold)
                {
                    continue;
                }

                foregroundPixels++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
                int quadrant = (x < bitmap.Width / 2 ? 0 : 1) + (y < bitmap.Height / 2 ? 0 : 2);
                quadrants[quadrant]++;
            }
        }

        double foregroundRatio = foregroundPixels / (double)totalPixels;
        double occupiedBoundsRatio = 0;
        double maxQuadrantRatio = 0;
        if (foregroundPixels > 0)
        {
            int boundsPixels = (right - left + 1) * (bottom - top + 1);
            occupiedBoundsRatio = boundsPixels / (double)totalPixels;
            int maxQuadrantPixels = 0;
            for (int i = 0; i < quadrants.Length; i++)
            {
                maxQuadrantPixels = Math.Max(maxQuadrantPixels, quadrants[i]);
            }

            maxQuadrantRatio = maxQuadrantPixels / (double)foregroundPixels;
        }
        else
        {
            left = top = right = bottom = 0;
        }

        List<string> warnings = [];
        if (visibleRatio < minimumVisibleRatio)
        {
            warnings.Add(
                $"Rendered frame is blank or near-black: {visibleRatio:P2} of pixels exceed luma threshold {nearBlackThreshold}. Verify visible enabled Elements at this scene time and check read_document_summary for fallback objects."
            );
        }
        else if (lumaRange < minimumContrastRange && lumaStdDev < minimumContrastStdDev)
        {
            warnings.Add(
                $"Rendered frame has very low visual contrast: luma range {lumaRange} and standard deviation {lumaStdDev:F2}. Verify foreground marks/text are visible against the background."
            );
        }

        if (foregroundPixels > 0
            && occupiedBoundsRatio < confinedBoundsRatio
            && maxQuadrantRatio >= confinedQuadrantRatio)
        {
            warnings.Add(
                $"Visible foreground is confined to a small single-quadrant area: occupied bounds {occupiedBoundsRatio:P1}, max quadrant share {maxQuadrantRatio:P1}. Consider spreading motion, accents, or typography across more of the frame."
            );
        }

        return new StillFrameVisibilityAnalysis(
            totalPixels,
            visiblePixels,
            visibleRatio,
            foregroundPixels,
            foregroundRatio,
            occupiedBoundsRatio,
            maxQuadrantRatio,
            left,
            top,
            right,
            bottom,
            minLuma,
            maxLuma,
            meanLuma,
            lumaStdDev,
            backgroundLuma,
            nearBlackThreshold,
            foregroundDeltaThreshold,
            warnings);
    }

    private static StillFrameVisibilityAnalysis EmptyAnalysis(
        string warning,
        int visibilityThreshold,
        int foregroundDeltaThreshold)
    {
        return new StillFrameVisibilityAnalysis(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            visibilityThreshold,
            foregroundDeltaThreshold,
            [warning]);
    }

    private static bool IsBorderPixel(int width, int height, int x, int y)
    {
        const int borderWidth = 2;
        return x < borderWidth
               || y < borderWidth
               || x >= width - borderWidth
               || y >= height - borderWidth;
    }

    private static IReadOnlyList<RenderStillActiveElement> CreateActiveElementSummaries(Scene scene, TimeSpan time)
    {
        return scene.Children
            .Where(element => element.IsEnabled && element.Range.Contains(time))
            .OrderBy(element => element.ZIndex)
            .Select(element => new RenderStillActiveElement(
                element.Id.ToString(),
                element.Name,
                element.Start.ToString("c"),
                element.Length.ToString("c"),
                element.ZIndex,
                element.Objects.Count))
            .ToArray();
    }

    private static int GetColorByteCount(BitmapColorType colorType, int bytesPerPixel)
    {
        return colorType switch
        {
            BitmapColorType.Alpha8 => bytesPerPixel,
            BitmapColorType.Rgb565 => bytesPerPixel,
            BitmapColorType.Argb4444 => bytesPerPixel,
            BitmapColorType.Rgba8888 => Math.Min(3, bytesPerPixel),
            BitmapColorType.Rgb888x => Math.Min(3, bytesPerPixel),
            BitmapColorType.Bgra8888 => Math.Min(3, bytesPerPixel),
            BitmapColorType.Rgba1010102 => bytesPerPixel,
            BitmapColorType.Bgra1010102 => bytesPerPixel,
            BitmapColorType.Rgb101010x => bytesPerPixel,
            BitmapColorType.Bgr101010x => bytesPerPixel,
            BitmapColorType.Bgr101010xXR => bytesPerPixel,
            BitmapColorType.Gray8 => Math.Min(1, bytesPerPixel),
            BitmapColorType.RgbaF16 => Math.Min(6, bytesPerPixel),
            BitmapColorType.RgbaF16Clamped => Math.Min(6, bytesPerPixel),
            BitmapColorType.RgbaF32 => Math.Min(12, bytesPerPixel),
            BitmapColorType.Rg88 => bytesPerPixel,
            BitmapColorType.AlphaF16 => bytesPerPixel,
            BitmapColorType.RgF16 => bytesPerPixel,
            BitmapColorType.Alpha16 => bytesPerPixel,
            BitmapColorType.Rg1616 => bytesPerPixel,
            BitmapColorType.Rgba16161616 => Math.Min(6, bytesPerPixel),
            BitmapColorType.Srgba8888 => Math.Min(3, bytesPerPixel),
            BitmapColorType.R8Unorm => Math.Min(1, bytesPerPixel),
            _ => Math.Min(3, bytesPerPixel)
        };
    }
}
