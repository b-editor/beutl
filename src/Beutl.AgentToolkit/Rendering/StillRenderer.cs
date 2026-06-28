using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed record RenderStillResponse(string OutputPath, int Width, int Height, string Time);

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

        return new RenderStillResponse(outputPath, snapshot.Width, snapshot.Height, time.ToString("c"));
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
}
