using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.ProxiesTab;

// Single source of truth for "which VideoSource values does an element (transitively) use?". Proxy
// resolution reaches SourceVideo drawables, VideoSourceNode graph inputs, and referenced scenes, and
// each holder can carry animated (keyframed) values as well as its current value. The walk mirrors
// the render path, so it descends into DrawableGroup children and node-graph GroupNode subgraphs too.
// Any caller that decides proxy usage (project summary, frame-cache invalidation) must cover all of them.
public static class ProxySourceEnumerator
{
    public static IEnumerable<VideoSource> EnumerateVideoSources(Element element, HashSet<Scene>? visitedScenes = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Enumerate(element, visitedScenes ?? new HashSet<Scene>(ReferenceEqualityComparer.Instance));
    }

    private static IEnumerable<VideoSource> Enumerate(Element element, HashSet<Scene> visitedScenes)
    {
        foreach (Drawable drawable in element.Objects.OfType<Drawable>())
        {
            foreach (VideoSource source in EnumerateDrawable(drawable, visitedScenes))
                yield return source;
        }
    }

    private static IEnumerable<VideoSource> EnumerateDrawable(Drawable drawable, HashSet<Scene> visitedScenes)
    {
        switch (drawable)
        {
            case SourceVideo video:
                foreach (VideoSource? source in EnumerateValues(video.Source))
                {
                    if (source != null)
                        yield return source;
                }

                break;

            case NodeGraphDrawable graphDrawable when graphDrawable.Model.CurrentValue is { } model:
                foreach (VideoSource source in EnumerateGraphSources(model))
                    yield return source;

                break;

            case SceneDrawable sceneDrawable when sceneDrawable.ReferencedScene.CurrentValue is { } referencedScene:
                // Scene references are user-constructible and can cycle; the visited set makes the walk
                // terminate (render-time Enter/Exit is the only other guard).
                if (visitedScenes.Add(referencedScene))
                {
                    foreach (Element child in referencedScene.Children)
                    {
                        foreach (VideoSource source in Enumerate(child, visitedScenes))
                            yield return source;
                    }
                }

                break;

            case DrawableGroup group:
                foreach (Drawable child in group.Children)
                {
                    foreach (VideoSource source in EnumerateDrawable(child, visitedScenes))
                        yield return source;
                }

                break;
        }

        // A VideoSourceNode can also live inside a NodeGraphFilterEffect on any drawable's filter
        // chain; the render path evaluates those with proxy flags, so they must be scanned too.
        foreach (VideoSource source in EnumerateFilterEffectGraphSources(drawable.FilterEffect.CurrentValue))
            yield return source;
    }

    private static IEnumerable<VideoSource> EnumerateGraphSources(GraphModel model)
    {
        foreach (GraphNode node in model.Nodes)
        {
            switch (node)
            {
                case VideoSourceNode { Source.Property: { } property }:
                    foreach (VideoSource? source in EnumerateValues(property))
                    {
                        if (source != null)
                            yield return source;
                    }

                    break;

                case GroupNode groupNode:
                    foreach (VideoSource source in EnumerateGraphSources(groupNode.Group))
                        yield return source;

                    break;
            }
        }
    }

    private static IEnumerable<VideoSource> EnumerateFilterEffectGraphSources(FilterEffect? effect)
    {
        if (effect is FilterEffectGroup group)
        {
            foreach (FilterEffect child in group.Children)
            {
                foreach (VideoSource source in EnumerateFilterEffectGraphSources(child))
                    yield return source;
            }
        }
        else if (effect is NodeGraphFilterEffect graphEffect
                 && graphEffect.Model.CurrentValue is { } model)
        {
            foreach (VideoSource source in EnumerateGraphSources(model))
                yield return source;
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
