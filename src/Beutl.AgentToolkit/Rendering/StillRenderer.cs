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
    IReadOnlyList<string> Warnings);

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

        return new RenderStillResponse(
            outputPath,
            snapshot.Width,
            snapshot.Height,
            time.ToString("c"),
            AnalyzeFrameWarnings(snapshot));
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

    private static IReadOnlyList<string> AnalyzeFrameWarnings(Bitmap bitmap)
    {
        const int nearBlackThreshold = 8;
        const double minimumVisibleRatio = 0.005;

        int totalPixels = bitmap.Width * bitmap.Height;
        if (totalPixels == 0)
        {
            return ["Rendered frame has no pixels."];
        }

        int colorByteCount = GetColorByteCount(bitmap.ColorType, bitmap.BytesPerPixel);
        if (colorByteCount <= 0)
        {
            return ["Rendered frame uses a pixel format that could not be analyzed for visibility."];
        }

        int visiblePixels = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int pixelOffset = x * bitmap.BytesPerPixel;
                for (int channel = 0; channel < colorByteCount; channel++)
                {
                    if (row[pixelOffset + channel] > nearBlackThreshold)
                    {
                        visiblePixels++;
                        break;
                    }
                }
            }
        }

        double visibleRatio = visiblePixels / (double)totalPixels;
        if (visibleRatio < minimumVisibleRatio)
        {
            return
            [
                $"Rendered frame is blank or near-black: {visibleRatio:P2} of pixels exceed luma threshold {nearBlackThreshold}. Verify visible enabled Elements at this scene time and check read_document_summary for fallback objects."
            ];
        }

        return [];
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
