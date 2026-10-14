using Beutl.Composition;
using Beutl.IO;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

/// <summary>
/// Collects the referenced media paths that can actually be rendered, skipping disabled, out-of-range, and
/// muted / non-solo top-level elements exactly as <c>SceneCompositor.SortLayers</c> does. The collectors walk
/// the mutable scene graph, so call them on the thread that owns the scene; existence is then checked
/// off-thread via <see cref="GetMissingPaths"/>.
/// </summary>
public static class ExportSourceValidator
{
    /// <summary>
    /// Collects the sources of the graphics pass for the single frame at <paramref name="time"/>.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, TimeSpan time)
        => CollectRenderableSources(
            scene, new TimeRange(time, TimeSpan.Zero), element => element.Range.Contains(time),
            [CompositionTarget.Graphics]);

    /// <summary>
    /// Collects the sources rendered anywhere in <paramref name="range"/>. Both the graphics and audio passes
    /// are walked, since a layer muted for only one pass still contributes the other pass's sources.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, TimeRange range)
        => CollectRenderableSources(scene, range, includeVideo: true, includeAudio: true);

    /// <summary>Collects only sources used by the selected export passes.</summary>
    public static IReadOnlySet<string> CollectRenderableSources(
        Scene scene, TimeRange range, bool includeVideo, bool includeAudio)
        => CollectRenderableSources(scene, range, element => element.Range.Intersects(range),
            (includeVideo, includeAudio) switch
            {
                (true, true) => [CompositionTarget.Graphics, CompositionTarget.Audio],
                (true, false) => [CompositionTarget.Graphics],
                (false, true) => [CompositionTarget.Audio],
                _ => [],
            });

    private static IReadOnlySet<string> CollectRenderableSources(
        Scene scene, TimeRange sceneWindow, Func<Element, bool> inRange, CompositionTarget[] targets)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var visitedScenes = new HashSet<(Scene, CompositionTarget?)>();
        SceneLayerSkipModel layerSkip = SceneLayerSkipModel.Build(scene);
        foreach (Element element in scene.Children)
        {
            if (!element.IsEnabled || !inRange(element))
                continue;

            // The render window in element-local time; a range window is first clamped to the element's
            // interval. Global-clock keyframes are filtered by sceneWindow, which stays in scene time.
            TimeRange elementLocalWindow = sceneWindow.Duration <= TimeSpan.Zero
                ? sceneWindow
                : ClampToElement(sceneWindow, element.Range);
            TimeRange localRange = elementLocalWindow.SubtractStart(element.Start);

            foreach (CompositionTarget target in targets)
            {
                // SceneCompositor.SortLayers drops a muted / non-solo element from this pass, so the
                // renderer never reads its sources; preflight must match or a missing file on such an
                // element would block an export that would render fine without it.
                if (layerSkip.ShouldSkip(element.ZIndex, target))
                    continue;

                foreach (IFileSource source in ProxySourceEnumerator.EnumerateFileSources(
                    element, visitedScenes, skipDisabledElements: true, target, localRange, sceneWindow))
                {
                    if (source.Uri is { IsFile: true } uri)
                        paths.Add(uri.LocalPath);
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Returns the subset of <paramref name="paths"/> whose files do not exist, sorted ordinally.
    /// Pure <see cref="File.Exists"/> checks with no scene-graph access, so it is safe to run on a
    /// background thread after <see cref="CollectRenderableSources(Scene, TimeRange)"/> snapshots the
    /// paths on the scene-owning thread.
    /// </summary>
    public static IReadOnlyList<string> GetMissingPaths(IReadOnlySet<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return paths
            .Where(static path => !File.Exists(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    // The overlap of the render window with the element's active interval: the render only samples the
    // element there, so animated keyframes are windowed to it. The caller already filtered to
    // intersecting elements, so the overlap is non-empty.
    private static TimeRange ClampToElement(TimeRange sceneWindow, TimeRange elementRange)
    {
        TimeSpan start = sceneWindow.Start > elementRange.Start ? sceneWindow.Start : elementRange.Start;
        TimeSpan end = sceneWindow.End < elementRange.End ? sceneWindow.End : elementRange.End;
        return new TimeRange(start, end - start);
    }
}
