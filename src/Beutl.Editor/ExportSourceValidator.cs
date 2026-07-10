using Beutl.Composition;
using Beutl.IO;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

public static class ExportSourceValidator
{
    /// <summary>
    /// Collects the referenced media paths that can actually be rendered for a single frame at
    /// <paramref name="time"/>, skipping disabled, out-of-range, and muted / non-solo top-level elements
    /// exactly as <c>SceneCompositor.SortLayers</c> does for the graphics pass. Walks the mutable scene
    /// graph, so the caller must run this on the thread that owns the scene; existence is then checked
    /// off-thread via <see cref="GetMissingPaths"/>.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, TimeSpan time)
        => CollectRenderableSources(
            scene, new TimeRange(time, TimeSpan.Zero), element => element.Range.Contains(time),
            [CompositionTarget.Graphics]);

    /// <summary>
    /// Collects the referenced media paths that can actually be rendered anywhere in
    /// <paramref name="range"/>, skipping disabled, out-of-range, and muted / non-solo top-level elements
    /// exactly as <c>SceneCompositor.SortLayers</c> does. A layer muted for only one pass (video-muted but
    /// audio-live, or vice versa) still contributes the other pass's sources, so both the graphics and
    /// audio passes are walked. Walks the mutable scene graph, so the caller must run this on the thread
    /// that owns the scene; existence is then checked off-thread via <see cref="GetMissingPaths"/>.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, TimeRange range)
        => CollectRenderableSources(scene, range, element => element.Range.Intersects(range),
            [CompositionTarget.Graphics, CompositionTarget.Audio]);

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

            // The render window in the element's local time (keyframe times are element-local): an
            // out-of-window animated source keyframe is then dropped as unreachable for this render.
            // Clamp the scene window to the element's active interval first (SubtractStart is a pure
            // shift): a range window wider than the element would otherwise map to pre-Start local time
            // (negative) and keep keyframes the element never renders. A point sample already passed the
            // Range.Contains gate, so it needs no clamp. sceneWindow (unclamped) still drives the
            // global-clock keyframe filter, which samples at scene time.
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

    // Mirror of SceneCompositor's per-frame layer snapshot + ShouldSkipLayer so preflight excludes exactly
    // the elements the compositor would: with any solo layer active only solo layers render; otherwise the
    // per-target mute flag (video for the graphics pass, audio for the audio pass) decides. Layers are
    // keyed by ZIndex, first one per ZIndex winning, matching GetLayerSnapshot.
    private readonly struct LayerSkipModel
    {
        private readonly Dictionary<int, TimelineLayer> _byZIndex;
        private readonly bool _hasSolo;

        private LayerSkipModel(Dictionary<int, TimelineLayer> byZIndex, bool hasSolo)
        {
            _byZIndex = byZIndex;
            _hasSolo = hasSolo;
        }

        public static LayerSkipModel Build(Scene scene)
        {
            var byZIndex = new Dictionary<int, TimelineLayer>(scene.Layers.Count);
            bool hasSolo = false;
            foreach (TimelineLayer layer in scene.Layers)
            {
                if (byZIndex.TryAdd(layer.ZIndex, layer) && layer.IsSolo)
                    hasSolo = true;
            }

            return new LayerSkipModel(byZIndex, hasSolo);
        }

        public bool ShouldSkip(int zIndex, CompositionTarget target)
        {
            _byZIndex.TryGetValue(zIndex, out TimelineLayer? layer);
            if (_hasSolo && (layer is null || !layer.IsSolo)) return true;
            if (layer is null) return false;
            return target == CompositionTarget.Graphics ? layer.IsVideoMuted : layer.IsAudioMuted;
        }
    }
}
