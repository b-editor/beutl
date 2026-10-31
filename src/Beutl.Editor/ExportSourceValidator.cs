using Beutl.Composition;
using Beutl.IO;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

/// <summary>
/// Collects the referenced media paths that can actually be rendered, skipping disabled, out-of-range, and
/// unused hidden graphics, and muted / non-solo output, while following required matte layers. The collectors walk
/// the mutable scene graph, so call them on the thread that owns the scene; existence is then checked
/// off-thread via <see cref="GetMissingPaths"/>.
/// </summary>
public static class ExportSourceValidator
{
    /// <summary>
    /// Collects the sources of the graphics pass for the single frame at <paramref name="time"/>.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, MediaTime time)
        => CollectRenderableSources(
            scene, new TimeRange(time, MediaTime.Zero),
            [CompositionTarget.Graphics]);

    /// <summary>
    /// Collects the sources rendered anywhere in <paramref name="range"/>. Both the graphics and audio passes
    /// are walked, since a layer muted for only one pass still contributes the other pass's sources.
    /// </summary>
    public static IReadOnlySet<string> CollectRenderableSources(Scene scene, TimeRange range)
        => CollectRenderableSources(scene, range, includeVideo: true, includeAudio: true);

    /// <summary>Collects only sources used by the selected export passes.</summary>
    public static IReadOnlySet<string> CollectRenderableSources(
        Scene scene, TimeRange range, bool includeVideo, bool includeAudio, Rational? frameRate = null)
        => CollectRenderableSources(scene, range,
            (includeVideo, includeAudio) switch
            {
                (true, true) => [CompositionTarget.Graphics, CompositionTarget.Audio],
                (true, false) => [CompositionTarget.Graphics],
                (false, true) => [CompositionTarget.Audio],
                _ => [],
            }, frameRate);

    private static IReadOnlySet<string> CollectRenderableSources(
        Scene scene, TimeRange sceneWindow, CompositionTarget[] targets, Rational? frameRate = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var visitedScenes = new HashSet<(Scene, CompositionTarget?)>();
        Rational rate = frameRate ?? scene.FindHierarchicalParent<Project>().GetFrameRate();
        // Validate against the renderer's positive-rate contract before walking any sources.
        // Audio-only output does not use the video rate.
        if (targets.Contains(CompositionTarget.Graphics))
            _ = MediaTime.FromFrames(0, rate);

        foreach (CompositionTarget target in targets)
        {
            foreach (IFileSource source in ProxySourceEnumerator.EnumerateRenderableSceneSources(
                         scene, visitedScenes, target, sceneWindow, rate, sceneWindow.Start))
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
