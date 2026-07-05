using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.ProxiesTab;

// Single source of truth for "which VideoSource values does an element (transitively) use?". Proxy
// resolution reaches SourceVideo drawables, VideoSourceNode graph inputs, and referenced scenes, and
// each holder can carry animated (keyframed) values as well as its current value. Any caller that
// decides proxy usage (project summary, frame-cache invalidation) must cover all of them.
public static class ProxySourceEnumerator
{
    public static IEnumerable<VideoSource> EnumerateVideoSources(Element element, HashSet<Scene>? visitedScenes = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Enumerate(element, visitedScenes ?? new HashSet<Scene>(ReferenceEqualityComparer.Instance));
    }

    private static IEnumerable<VideoSource> Enumerate(Element element, HashSet<Scene> visitedScenes)
    {
        foreach (SourceVideo video in element.Objects.OfType<SourceVideo>())
        {
            foreach (VideoSource? source in EnumerateValues(video.Source))
            {
                if (source != null)
                    yield return source;
            }
        }

        foreach (NodeGraphDrawable graphDrawable in element.Objects.OfType<NodeGraphDrawable>())
        {
            GraphModel? model = graphDrawable.Model.CurrentValue;
            if (model == null)
                continue;

            foreach (VideoSourceNode node in model.Nodes.OfType<VideoSourceNode>())
            {
                if (node.Source.Property == null)
                    continue;

                foreach (VideoSource? source in EnumerateValues(node.Source.Property))
                {
                    if (source != null)
                        yield return source;
                }
            }
        }

        foreach (SceneDrawable sceneDrawable in element.Objects.OfType<SceneDrawable>())
        {
            if (sceneDrawable.ReferencedScene.CurrentValue is not { } referencedScene)
                continue;

            // Scene references are user-constructible and can cycle; the visited set makes the walk
            // terminate (render-time Enter/Exit is the only other guard).
            if (!visitedScenes.Add(referencedScene))
                continue;

            foreach (Element child in referencedScene.Children)
            {
                foreach (VideoSource source in Enumerate(child, visitedScenes))
                    yield return source;
            }
        }
    }

    private static IEnumerable<VideoSource?> EnumerateValues(IProperty<VideoSource?> property)
    {
        yield return property.CurrentValue;

        foreach (VideoSource? source in EnumerateAnimatedValues(property.Animation))
            yield return source;
    }

    private static IEnumerable<VideoSource?> EnumerateValues(IPropertyAdapter<VideoSource?> property)
    {
        yield return property.GetValue();

        if (property is IAnimatablePropertyAdapter<VideoSource?> animatable)
        {
            foreach (VideoSource? source in EnumerateAnimatedValues(animatable.Animation))
                yield return source;
        }
    }

    private static IEnumerable<VideoSource?> EnumerateAnimatedValues(IAnimation<VideoSource?>? animation)
    {
        if (animation is not KeyFrameAnimation<VideoSource?> keyFrameAnimation)
            yield break;

        foreach (IKeyFrame keyFrame in keyFrameAnimation.KeyFrames)
        {
            if (keyFrame.Value is VideoSource source)
                yield return source;
        }
    }
}
