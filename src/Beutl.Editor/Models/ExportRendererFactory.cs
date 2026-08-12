using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.ProjectSystem;

namespace Beutl.Models;

/// <summary>
/// Creates the <see cref="SceneRenderer"/> used by delivery-grade output paths (video export, still
/// export, frame save).
/// </summary>
public static class ExportRendererFactory
{
    /// <summary>
    /// Creates a renderer configured for final output: <see cref="RenderIntent.Delivery"/> so an
    /// intermediate render-target allocation failure fails the export instead of silently dropping the
    /// affected content, <see cref="WorkingScaleCeiling.Export"/>, original media (the default
    /// PreferProxy setting would otherwise encode from preview proxies), an unshared resource graph so
    /// live preview resources are untouched, and no render caching.
    /// </summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="renderScale">Output scale in device px per logical unit.</param>
    public static SceneRenderer Create(Scene scene, float renderScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var renderer = new SceneRenderer(
            scene,
            renderScale,
            disableResourceShare: true,
            maxWorkingScale: WorkingScaleCeiling.Export(),
            forceOriginalSource: true,
            intent: RenderIntent.Delivery);
        try
        {
            renderer.CacheOptions = RenderCacheOptions.Disabled;
        }
        catch
        {
            DisposePreservingPrimaryFailure(renderer);
            throw;
        }

        return renderer;
    }

    private static void DisposePreservingPrimaryFailure(IDisposable? value)
    {
        try
        {
            value?.Dispose();
        }
        catch
        {
            // Cleanup must not replace the failure that triggered it.
        }
    }
}
