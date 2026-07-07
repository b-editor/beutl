using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

// Single source of truth for "which media does this reference?". Proxy resolution reaches
// SourceVideo drawables, VideoSourceNode graph inputs, and referenced scenes, and each holder
// can carry animated (keyframed) values as well as its current value. The walk mirrors the render
// path, so it descends into DrawableGroup children and node-graph GroupNode subgraphs too.
// Any caller that decides proxy usage (project summary, frame-cache invalidation, eviction
// protection) must cover all of them.
public static class ProxySourceEnumerator
{
    public static IEnumerable<VideoSource> EnumerateVideoSources(Element element, HashSet<Scene>? visitedScenes = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Enumerate(element, visitedScenes ?? new HashSet<Scene>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Collects the file-system paths of every <see cref="IFileSource"/> referenced anywhere in
    /// <paramref name="root"/>, regardless of whether each file lives inside or outside the project
    /// directory, AND every <see cref="VideoSource"/> held in a node-graph adapter the broad
    /// <see cref="IFileSource"/> walk cannot reach. This is the single walk that subsumes the old
    /// Engine-only <c>CollectProjectFileSources</c> + the UI video-only enumerator union: in-project
    /// media must be covered too (otherwise a project whose media lives under its own folder gets
    /// no affinity protection), and graph-only clips must be covered (their <see cref="VideoSource"/>
    /// lives in a <see cref="NodeGraph"/> port, not a plain <see cref="IProperty{T}"/> on an
    /// <see cref="EngineObject"/>). Paths are deduped with <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    public static IReadOnlySet<string> EnumerateFileSources(IHierarchical root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (CoreObject obj in root.EnumerateAllChildren<CoreObject>())
            CollectFileSourcePaths(obj, paths);

        if (root is CoreObject rootObj)
            CollectFileSourcePaths(rootObj, paths);

        // The broad IFileSource walk above cannot see VideoSource values held in NodeGraph adapters
        // (a VideoSourceNode port is an IPropertyAdapter, not a plain IProperty on EngineObject.Properties),
        // so run the video walk for each Element and fold in its URIs. Dedup is automatic.
        foreach (Element element in root.EnumerateAllChildren<Element>())
        {
            foreach (VideoSource source in EnumerateVideoSources(element))
            {
                if (source is { HasUri: true } && source.Uri is { IsFile: true } uri)
                    paths.Add(uri.LocalPath);
            }
        }

        return paths;
    }

    private static void CollectFileSourcePaths(CoreObject obj, HashSet<string> paths)
    {
        if (obj is EngineObject engineObj)
        {
            foreach (IProperty property in engineObj.Properties)
            {
                if (property.CurrentValue is IFileSource fileSource)
                    AddFileSourcePath(fileSource.Uri, paths);

                // Rendering (and the proxy scanner) consume animated file-source values too, so media
                // referenced only from keyframes must be protected — otherwise its in-project proxy is
                // treated as unprotected and can be evicted before unrelated closed-project proxies.
                if (property.Animation is KeyFrameAnimation keyFrameAnimation)
                {
                    foreach (IKeyFrame keyFrame in keyFrameAnimation.KeyFrames)
                    {
                        if (keyFrame.Value is IFileSource keyFrameSource)
                            AddFileSourcePath(keyFrameSource.Uri, paths);
                    }
                }
            }
        }

        AddFileSourcePath(obj.Uri, paths);

        foreach (CoreProperty prop in PropertyRegistry.GetRegistered(obj.GetType()))
        {
            if (prop.PropertyType.IsValueType)
                continue;

            if (obj.GetValue(prop) is IFileSource fileSource)
                AddFileSourcePath(fileSource.Uri, paths);
        }
    }

    private static void AddFileSourcePath(Uri? uri, HashSet<string> paths)
    {
        if (uri is { IsFile: true })
            paths.Add(uri.LocalPath);
    }

    private static IEnumerable<VideoSource> Enumerate(Element element, HashSet<Scene> visitedScenes)
    {
        foreach (Drawable drawable in element.Objects.OfType<Drawable>())
        {
            foreach (VideoSource source in EnumerateDrawable(drawable, visitedScenes, new(ReferenceEqualityComparer.Instance), new(ReferenceEqualityComparer.Instance)))
                yield return source;
        }
    }

    private static IEnumerable<VideoSource> EnumerateDrawable(
        Drawable drawable,
        HashSet<Scene> visitedScenes,
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<FilterEffectGroup> visitedFilterEffectGroups)
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
                foreach (VideoSource source in EnumerateGraphSources(model, visitedGraphGroups))
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
                    foreach (VideoSource source in EnumerateDrawable(child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups))
                        yield return source;
                }

                break;
        }

        // A VideoSourceNode can also live inside a NodeGraphFilterEffect on any drawable's filter
        // chain; the render path evaluates those with proxy flags, so they must be scanned too.
        foreach (VideoSource source in EnumerateFilterEffectGraphSources(drawable.FilterEffect.CurrentValue, visitedFilterEffectGroups, visitedGraphGroups))
            yield return source;
    }

    private static IEnumerable<VideoSource> EnumerateGraphSources(
        GraphModel model,
        HashSet<GraphGroup> visitedGraphGroups)
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
                    // A group can receive VideoSource values at its outer input boundary per instance.
                    // Scan those inputs for every GroupNode, even when several instances share the same
                    // GraphGroup and recursion into the shared inner graph is already guarded.
                    foreach (VideoSource source in EnumerateGroupNodeInputSources(groupNode))
                        yield return source;

                    // A user-constructed GroupNode can reference a GraphGroup that (transitively)
                    // contains the same GroupNode, producing an infinite walk. The visited set
                    // makes the recursion terminate, mirroring the Scene cycle-break above.
                    if (visitedGraphGroups.Add(groupNode.Group))
                    {
                        foreach (VideoSource source in EnumerateGraphSources(groupNode.Group, visitedGraphGroups))
                            yield return source;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<VideoSource> EnumerateGroupNodeInputSources(GroupNode groupNode)
    {
        foreach (var member in groupNode.Items)
        {
            if (member is not IInputPort { Property: { } property })
                continue;

            // Gate via PropertyType rather than a typed pattern: IPropertyAdapter<T> is invariant, so
            // `is IPropertyAdapter<VideoSource?>` would miss a non-nullable adapter. PropertyType ==
            // typeof(VideoSource) holds for both VideoSource and VideoSource? ports.
            if (property.PropertyType != typeof(VideoSource))
                continue;

            if (property.GetValue() is VideoSource current)
                yield return current;

            if (property is IAnimatablePropertyAdapter<VideoSource?> { Animation: { } animation })
            {
                foreach (VideoSource? source in EnumerateAnimatedValues(animation))
                {
                    if (source != null)
                        yield return source;
                }
            }
        }
    }

    private static IEnumerable<VideoSource> EnumerateFilterEffectGraphSources(
        FilterEffect? effect,
        HashSet<FilterEffectGroup> visitedFilterEffectGroups,
        HashSet<GraphGroup> visitedGraphGroups)
    {
        if (effect is FilterEffectGroup group && visitedFilterEffectGroups.Add(group))
        {
            foreach (FilterEffect child in group.Children)
            {
                foreach (VideoSource source in EnumerateFilterEffectGraphSources(child, visitedFilterEffectGroups, visitedGraphGroups))
                    yield return source;
            }
        }
        else if (effect is NodeGraphFilterEffect graphEffect
                 && graphEffect.Model.CurrentValue is { } model)
        {
            foreach (VideoSource source in EnumerateGraphSources(model, visitedGraphGroups))
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
