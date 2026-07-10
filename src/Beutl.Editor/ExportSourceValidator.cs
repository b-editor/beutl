using Beutl.Composition;
using Beutl.IO;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

public static class ExportSourceValidator
{
    /// <summary>
    /// Collects the referenced media paths that can actually be rendered for a single frame at
    /// <paramref name="time"/>, skipping disabled or out-of-range top-level elements exactly as
    /// <c>SceneCompositor.SortLayers</c> does. Walks the mutable scene graph, so the caller must run
    /// this on the thread that owns the scene; existence is then checked off-thread via
    /// <see cref="GetMissingPaths"/>.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, TimeSpan time)
        => CollectRenderableSources(
            scene, new TimeRange(time, TimeSpan.Zero), element => element.Range.Contains(time), CompositionTarget.Graphics);

    /// <summary>
    /// Collects the referenced media paths that can actually be rendered anywhere in
    /// <paramref name="range"/>, skipping disabled or out-of-range top-level elements exactly as
    /// <c>SceneCompositor.SortLayers</c> does. Walks the mutable scene graph, so the caller must run
    /// this on the thread that owns the scene; existence is then checked off-thread via
    /// <see cref="GetMissingPaths"/>.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, TimeRange range)
        => CollectRenderableSources(scene, range, element => element.Range.Intersects(range));

    private static IReadOnlySet<string> CollectRenderableSources(
        Scene scene, TimeRange sceneWindow, Func<Element, bool> inRange, CompositionTarget? renderTarget = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var visitedScenes = new HashSet<(Scene, CompositionTarget?)>();
        foreach (Element element in scene.Children)
        {
            if (!element.IsEnabled || !inRange(element))
                continue;

            // The render window in the element's local time (keyframe times are element-local): an
            // out-of-window animated source keyframe is then dropped as unreachable for this render.
            // sceneWindow itself is the scene-time window a global-clock keyframe samples against.
            TimeRange localRange = sceneWindow.SubtractStart(element.Start);
            foreach (IFileSource source in ProxySourceEnumerator.EnumerateFileSources(
                element, visitedScenes, skipDisabledElements: true, renderTarget, localRange, sceneWindow))
            {
                if (source.Uri is { IsFile: true } uri)
                    paths.Add(uri.LocalPath);
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
}
