using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
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

public sealed record RenderedTextBounds(
    Element Element,
    TextBlock TextBlock,
    Rect Bounds);

public sealed class RenderedFrameAnalysis : IDisposable
{
    public RenderedFrameAnalysis(TimeSpan time, Bitmap bitmap, IReadOnlyList<RenderedTextBounds> textBounds)
    {
        Time = time;
        Bitmap = bitmap;
        TextBounds = textBounds;
    }

    public TimeSpan Time { get; }

    public Bitmap Bitmap { get; }

    public IReadOnlyList<RenderedTextBounds> TextBounds { get; }

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}

public sealed record RenderStoryboardResponse(
    string ContactSheetPath,
    IReadOnlyList<RenderStoryboardShot> Shots,
    IReadOnlyList<CutEyeTrace> CutEyeTrace,
    IReadOnlyList<string> ReviewNotes);

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

public sealed record NormalizedFocalPoint(
    double X,
    double Y);

public sealed record CutEyeTrace(
    string LeftFrame,
    string RightFrame,
    NormalizedFocalPoint LeftFocalPoint,
    NormalizedFocalPoint RightFocalPoint,
    double DisplacementRatio,
    bool ExceedsEyeTraceBudget);

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
        if (ContainsGpuOnlyContent(scene, time)
            && !await Has3DGraphicsContextAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new RenderingUnavailableException(
                "The scene contains 3D content, but no GPU context with 3D rendering support is available.");
        }

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            // Agent still render is a final output, so force original media (proxies are preview-only);
            // otherwise the default PreferProxy setting would decode cached proxies here.
            using var renderer = new SceneRenderer(
                scene, normalizedScale, disableResourceShare: true, maxWorkingScale: float.PositiveInfinity,
                forceOriginalSource: true, renderIntent: RenderIntent.Delivery);
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            ThrowIfSourcesMissing(scene, time + scene.Start);
            var frame = renderer.Compositor.EvaluateGraphics(time + scene.Start);
            renderer.Render(frame);
            return renderer.Snapshot();
        }, ct: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RenderedFrameAnalysis> RenderFrameAnalysisAsync(
        Scene scene,
        TimeSpan time,
        float renderScale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ContainsGpuOnlyContent(scene, time)
            && !await Has3DGraphicsContextAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new RenderingUnavailableException(
                "The scene contains 3D content, but no GPU context with 3D rendering support is available.");
        }

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            // Agent still render is a final output, so force original media (proxies are preview-only);
            // otherwise the default PreferProxy setting would decode cached proxies here.
            using var renderer = new SceneRenderer(
                scene, normalizedScale, disableResourceShare: true, maxWorkingScale: float.PositiveInfinity,
                forceOriginalSource: true, renderIntent: RenderIntent.Delivery);
            renderer.CacheOptions = RenderCacheOptions.Disabled;

            ThrowIfSourcesMissing(scene, time + scene.Start);
            var frame = renderer.Compositor.EvaluateGraphics(time + scene.Start);
            renderer.Render(frame);
            IReadOnlyList<RenderedTextBounds> textBounds = CreateRenderedTextBounds(scene, renderer, time);
            return new RenderedFrameAnalysis(time, renderer.Snapshot(), textBounds);
        }, ct: cancellationToken).ConfigureAwait(false);
    }

    // relativeTime scopes the check to elements active at that scene-relative sample; null checks the
    // whole visible window [Scene.Start, Scene.Start + Duration) (exports render the full window).
    // A Scene3D on a disabled or never-rendered element must not force the GPU requirement.
    internal static bool ContainsGpuOnlyContent(Scene scene, TimeSpan? relativeTime = null)
    {
        TimeSpan windowStart = scene.Start;
        TimeSpan windowEnd = scene.Start + scene.Duration;
        return scene.Children
            .Where(element => element.IsEnabled)
            .Where(element => relativeTime is { } time
                ? element.Range.Contains(time + scene.Start)
                : scene.Duration <= TimeSpan.Zero
                  || (element.Start < windowEnd && element.Start + element.Length > windowStart))
            .SelectMany(element => element.Objects)
            .Any(ContainsEnabledGpuContent);
    }

    // A final render forces original media (no proxy fallback), so a moved/deleted original that a Ready
    // proxy would have stood in for during preview renders a blank frame instead of failing. Preflight the
    // frame's renderable sources on the render thread and fail fast, matching the save-frame/export guard.
    private static void ThrowIfSourcesMissing(Scene scene, TimeSpan sceneTime)
    {
        IReadOnlyList<string> missing = Beutl.Editor.ExportSourceValidator.GetMissingPaths(
            Beutl.Editor.ExportSourceValidator.CollectRenderableSources(scene, sceneTime));
        if (missing.Count > 0)
        {
            throw new RenderingUnavailableException(
                $"Missing source files required to render: {string.Join(", ", missing)}");
        }
    }

    // The renderer skips a disabled object (EngineObject.IsEnabled) and everything under it, so a
    // disabled Scene3D — or a Scene3D under a disabled group — must not force the GPU requirement.
    // EnumerateAllChildren walks disabled subtrees, so recurse manually and prune them.
    private static bool ContainsEnabledGpuContent(IHierarchical node)
    {
        if (node is EngineObject { IsEnabled: false })
        {
            return false;
        }

        if (node is Scene3D)
        {
            return true;
        }

        foreach (IHierarchical child in node.HierarchicalChildren)
        {
            if (ContainsEnabledGpuContent(child))
            {
                return true;
            }
        }

        return false;
    }

    internal static async ValueTask<bool> Has3DGraphicsContextAsync(CancellationToken cancellationToken)
    {
        return await RenderThread.Dispatcher.InvokeAsync(
            () => GraphicsContextFactory.GetOrCreateShared()?.Supports3DRendering == true,
            ct: cancellationToken).ConfigureAwait(false);
    }

    internal static StillFrameVisibilityAnalysis AnalyzeFrameVisibility(Bitmap bitmap)
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

    internal static NormalizedFocalPoint EstimateFocalPoint(
        Scene scene,
        RenderedFrameAnalysis frame,
        StillFrameVisibilityAnalysis visibility)
    {
        if (TryEstimateTextFocalPoint(scene, frame.TextBounds, out NormalizedFocalPoint textFocalPoint))
        {
            return textFocalPoint;
        }

        return EstimateForegroundFocalPoint(frame.Bitmap, visibility);
    }

    private static bool TryEstimateTextFocalPoint(
        Scene scene,
        IReadOnlyList<RenderedTextBounds> textBounds,
        out NormalizedFocalPoint focalPoint)
    {
        RenderedTextBounds? best = textBounds
            .Where(item => item.TextBlock.IsEnabled)
            .Where(item => !string.IsNullOrWhiteSpace(item.TextBlock.Text.CurrentValue))
            .Where(item => item.Bounds.Width >= 4 && item.Bounds.Height >= 4)
            .OrderByDescending(item => item.Element.ZIndex)
            .ThenByDescending(item => item.Bounds.Width * item.Bounds.Height)
            .FirstOrDefault();
        if (best is null)
        {
            focalPoint = new NormalizedFocalPoint(0.5, 0.5);
            return false;
        }

        double width = scene.FrameSize.Width > 0 ? scene.FrameSize.Width : best.Bounds.Right;
        double height = scene.FrameSize.Height > 0 ? scene.FrameSize.Height : best.Bounds.Bottom;
        focalPoint = new NormalizedFocalPoint(
            NormalizeUnit(best.Bounds.Center.X, width),
            NormalizeUnit(best.Bounds.Center.Y, height));
        return true;
    }

    private static NormalizedFocalPoint EstimateForegroundFocalPoint(
        Bitmap bitmap,
        StillFrameVisibilityAnalysis visibility)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || visibility.ForegroundPixels <= 0)
        {
            return new NormalizedFocalPoint(0.5, 0.5);
        }

        int colorByteCount = GetColorByteCount(bitmap.ColorType, bitmap.BytesPerPixel);
        if (colorByteCount <= 0)
        {
            return FocalFromVisibilityBounds(bitmap, visibility);
        }

        int totalPixels = bitmap.Width * bitmap.Height;
        var state = new byte[totalPixels];
        var lumaByPixel = new byte[totalPixels];
        double globalWeight = 0;
        double globalX = 0;
        double globalY = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int index = (y * bitmap.Width) + x;
                int offset = x * bitmap.BytesPerPixel;
                int luma = 0;
                for (int channel = 0; channel < colorByteCount; channel++)
                {
                    luma += row[offset + channel];
                }

                luma /= colorByteCount;
                lumaByPixel[index] = (byte)Math.Clamp(luma, 0, byte.MaxValue);
                double delta = Math.Abs(luma - visibility.BackgroundLuma);
                if (delta <= visibility.ForegroundDeltaThreshold)
                {
                    continue;
                }

                state[index] = 1;
                double weight = Math.Max(1, delta);
                globalWeight += weight;
                globalX += (x + 0.5) * weight;
                globalY += (y + 0.5) * weight;
            }
        }

        if (globalWeight <= 0)
        {
            return FocalFromVisibilityBounds(bitmap, visibility);
        }

        double globalCentroidX = globalX / globalWeight;
        double globalCentroidY = globalY / globalWeight;
        double bestWeight = 0;
        double bestX = 0;
        double bestY = 0;
        var stack = new List<int>();
        for (int index = 0; index < state.Length; index++)
        {
            if (state[index] != 1)
            {
                continue;
            }

            double componentWeight = 0;
            double componentX = 0;
            double componentY = 0;
            stack.Clear();
            stack.Add(index);
            state[index] = 2;
            while (stack.Count > 0)
            {
                int current = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                int x = current % bitmap.Width;
                int y = current / bitmap.Width;
                double weight = Math.Max(1, Math.Abs(lumaByPixel[current] - visibility.BackgroundLuma));
                componentWeight += weight;
                componentX += (x + 0.5) * weight;
                componentY += (y + 0.5) * weight;

                AddForegroundNeighbor(x - 1, y);
                AddForegroundNeighbor(x + 1, y);
                AddForegroundNeighbor(x, y - 1);
                AddForegroundNeighbor(x, y + 1);
            }

            if (componentWeight > bestWeight)
            {
                bestWeight = componentWeight;
                bestX = componentX / componentWeight;
                bestY = componentY / componentWeight;
            }
        }

        double focalX = bestWeight > 0 ? (globalCentroidX * 0.35) + (bestX * 0.65) : globalCentroidX;
        double focalY = bestWeight > 0 ? (globalCentroidY * 0.35) + (bestY * 0.65) : globalCentroidY;
        return new NormalizedFocalPoint(
            NormalizeUnit(focalX, bitmap.Width),
            NormalizeUnit(focalY, bitmap.Height));

        void AddForegroundNeighbor(int x, int y)
        {
            if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
            {
                return;
            }

            int neighbor = (y * bitmap.Width) + x;
            if (state[neighbor] != 1)
            {
                return;
            }

            state[neighbor] = 2;
            stack.Add(neighbor);
        }
    }

    private static NormalizedFocalPoint FocalFromVisibilityBounds(
        Bitmap bitmap,
        StillFrameVisibilityAnalysis visibility)
    {
        if (visibility.Right <= visibility.Left || visibility.Bottom <= visibility.Top)
        {
            return new NormalizedFocalPoint(0.5, 0.5);
        }

        double x = (visibility.Left + visibility.Right + 1) / 2d;
        double y = (visibility.Top + visibility.Bottom + 1) / 2d;
        return new NormalizedFocalPoint(
            NormalizeUnit(x, bitmap.Width),
            NormalizeUnit(y, bitmap.Height));
    }

    private static double NormalizeUnit(double value, double denominator)
    {
        if (!double.IsFinite(value) || !double.IsFinite(denominator) || denominator <= 0)
        {
            return 0.5;
        }

        return Math.Round(Math.Clamp(value / denominator, 0, 1), 4, MidpointRounding.AwayFromZero);
    }

    private static bool IsBorderPixel(int width, int height, int x, int y)
    {
        const int borderWidth = 2;
        return x < borderWidth
               || y < borderWidth
               || x >= width - borderWidth
               || y >= height - borderWidth;
    }

    internal static IReadOnlyList<RenderStillActiveElement> CreateActiveElementSummaries(Scene scene, TimeSpan time)
    {
        // The frame is rendered at time + scene.Start (EvaluateGraphics), so element activity must be
        // filtered at the same absolute time or a non-zero Scene.Start reports the wrong elements.
        TimeSpan renderTime = time + scene.Start;
        return scene.Children
            .Where(element => element.IsEnabled && element.Range.Contains(renderTime))
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

    private static IReadOnlyList<RenderedTextBounds> CreateRenderedTextBounds(
        Scene scene,
        SceneRenderer renderer,
        TimeSpan time)
    {
        TimeSpan renderTime = time + scene.Start;
        var result = new List<RenderedTextBounds>();
        foreach (Element element in scene.Children)
        {
            if (!element.IsEnabled || !element.Range.Contains(renderTime))
            {
                continue;
            }

            foreach (EngineObject obj in element.Objects)
            {
                CollectRenderedTextBounds(element, obj, renderer, result);
            }
        }

        return result;
    }

    // Flow operators (DrawableGroup / DrawableDecorator) render their children, so text nested under
    // a group (e.g. after duplicate_object(wrapInGroup:true)) must still be measured — otherwise
    // rendered contrast / eye-trace checks ignore grouped text that is actually on screen.
    private static void CollectRenderedTextBounds(
        Element element,
        EngineObject obj,
        SceneRenderer renderer,
        List<RenderedTextBounds> result)
    {
        if (obj is TextBlock textBlock && textBlock.IsEnabled
            && renderer.GetBoundary(textBlock) is { } bounds
            && bounds.Width > 0
            && bounds.Height > 0)
        {
            // Attribute the bound to the TextBlock's own owning element, not the outer loop element:
            // a grouped/portaled child can originate from a different element, and downstream
            // focal-point selection ranks by RenderedTextBounds.Element.ZIndex.
            Element owner = textBlock.FindHierarchicalParent<Element>() ?? element;
            result.Add(new RenderedTextBounds(owner, textBlock, bounds));
        }

        IEnumerable<EngineObject> children = obj switch
        {
            DrawableGroup group => group.Children,
            DrawableDecorator decorator => decorator.Children,
            _ => []
        };

        foreach (EngineObject child in children)
        {
            CollectRenderedTextBounds(element, child, renderer, result);
        }
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
