using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

// Single source of truth for "which media does this reference?". The walk mirrors the render path,
// so it reaches every IFileSource a renderer can open: object properties, animated values, node-graph
// adapter inputs, filter-effect subgraphs, and referenced scenes. Proxy resolution (video-only) and
// export preflight (all media) both build on this one traversal.
public static class ProxySourceEnumerator
{
    /// <summary>
    /// Enumerates every <see cref="VideoSource"/> reachable from <paramref name="element"/> — the
    /// proxy-eligible subset of the full media walk (proxies are generated for video only). Reaches
    /// direct/animated <see cref="SourceVideo"/> values, node-graph adapter inputs, filter-effect
    /// subgraphs, and referenced scenes.
    /// </summary>
    public static IEnumerable<VideoSource> EnumerateVideoSources(Element element, HashSet<Scene>? visitedScenes = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return EnumerateElementFileSources(element, visitedScenes ?? new HashSet<Scene>(ReferenceEqualityComparer.Instance))
            .OfType<VideoSource>();
    }

    /// <summary>
    /// Collects the file-system paths of every <see cref="IFileSource"/> referenced anywhere in
    /// <paramref name="root"/>, regardless of whether each file lives inside or outside the project
    /// directory. Covers the broad <see cref="IFileSource"/> property walk AND every source the
    /// property walk cannot reach — node-graph adapter inputs (a port is an <see cref="IPropertyAdapter"/>,
    /// not a plain <see cref="IProperty{T}"/> on an <see cref="EngineObject"/>) and media held inside
    /// referenced scenes (a referenced scene is a property value, not a hierarchical child). Paths are
    /// deduped with <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    public static IReadOnlySet<string> EnumerateFileSources(IHierarchical root)
        => EnumerateFileSources(root, includeObjectUris: true);

    /// <summary>
    /// Collects media file paths while excluding project document URIs such as scene and layer files.
    /// </summary>
    public static IReadOnlySet<string> EnumerateMediaFileSources(IHierarchical root)
        => EnumerateFileSources(root, includeObjectUris: false);

    private static IReadOnlySet<string> EnumerateFileSources(IHierarchical root, bool includeObjectUris)
    {
        ArgumentNullException.ThrowIfNull(root);

        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (CoreObject obj in root.EnumerateAllChildren<CoreObject>())
            CollectFileSourcePaths(obj, paths, includeObjectUris);

        if (root is CoreObject rootObj)
            CollectFileSourcePaths(rootObj, paths, includeObjectUris);

        // The property walk above cannot see node-graph adapter inputs (a port is an IPropertyAdapter,
        // not an EngineObject property) or media inside referenced scenes (a scene is a property value,
        // not a hierarchical child), so descend those here. A shared visited-scene set resolves cross-
        // element references to the same scene once.
        var visitedScenes = new HashSet<Scene>(ReferenceEqualityComparer.Instance);
        foreach (Element element in root.EnumerateAllChildren<Element>())
        {
            foreach (IFileSource source in EnumerateElementFileSources(element, visitedScenes))
            {
                if (source.Uri is { IsFile: true } uri)
                    paths.Add(uri.LocalPath);
            }
        }

        return paths;
    }

    private static void CollectFileSourcePaths(CoreObject obj, HashSet<string> paths, bool includeObjectUri)
    {
        if (obj is EngineObject engineObj)
        {
            foreach (IFileSource source in EnumeratePropertyFileSources(engineObj))
                AddFileSourcePath(source.Uri, paths);
        }

        if (includeObjectUri)
            AddFileSourcePath(obj.Uri, paths);
    }

    private static void AddFileSourcePath(Uri? uri, HashSet<string> paths)
    {
        if (uri is { IsFile: true })
            paths.Add(uri.LocalPath);
    }

    private static IEnumerable<IFileSource> EnumerateElementFileSources(Element element, HashSet<Scene> visitedScenes)
    {
        foreach (EngineObject obj in element.Objects)
        {
            foreach (IFileSource source in EnumerateObjectFileSources(
                obj,
                visitedScenes,
                new HashSet<GraphGroup>(ReferenceEqualityComparer.Instance),
                new HashSet<FilterEffectGroup>(ReferenceEqualityComparer.Instance)))
            {
                yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateObjectFileSources(
        EngineObject obj,
        HashSet<Scene> visitedScenes,
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<FilterEffectGroup> visitedFilterEffectGroups)
    {
        // Direct IFileSource-valued properties (current + animated): SourceVideo/SourceImage/SourceSound.
        foreach (IFileSource source in EnumeratePropertyFileSources(obj))
            yield return source;

        if (obj is Drawable drawable)
        {
            // A VideoSourceNode / ImageSourceNode can live inside a NodeGraphFilterEffect on any
            // drawable's filter chain; the render path evaluates those, so scan them too.
            foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                drawable.FilterEffect.CurrentValue, visitedFilterEffectGroups, visitedGraphGroups))
                yield return source;

            switch (drawable)
            {
                case NodeGraphDrawable { Model.CurrentValue: { } model }:
                    foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups))
                        yield return source;

                    break;

                case SceneDrawable { ReferencedScene.CurrentValue: { } referencedScene }:
                    foreach (IFileSource source in EnumerateReferencedSceneSources(referencedScene, visitedScenes))
                        yield return source;

                    break;

                case DrawableGroup group:
                    foreach (Drawable child in group.Children)
                    {
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups))
                            yield return source;
                    }

                    break;
            }
        }

        // A SceneSound is a Sound (not a Drawable), so it is not covered by the Drawable switch above;
        // its referenced scene still contributes renderable media and must be descended.
        if (obj is SceneSound { ReferencedScene.CurrentValue: { } soundScene })
        {
            foreach (IFileSource source in EnumerateReferencedSceneSources(soundScene, visitedScenes))
                yield return source;
        }
    }

    private static IEnumerable<IFileSource> EnumerateReferencedSceneSources(Scene scene, HashSet<Scene> visitedScenes)
    {
        // Scene references are user-constructible and can cycle; the visited set makes the walk
        // terminate (render-time Enter/Exit is the only other guard).
        if (!visitedScenes.Add(scene))
            yield break;

        foreach (Element child in scene.Children)
        {
            foreach (IFileSource source in EnumerateElementFileSources(child, visitedScenes))
                yield return source;
        }
    }

    private static IEnumerable<IFileSource> EnumeratePropertyFileSources(EngineObject obj)
    {
        foreach (IProperty property in obj.Properties)
        {
            if (property.CurrentValue is IFileSource fileSource)
                yield return fileSource;

            // Rendering (and the proxy scanner) consume animated file-source values too, so media
            // referenced only from keyframes must be enumerated as well.
            foreach (IFileSource animated in EnumerateAnimatedFileSources(property.Animation))
                yield return animated;
        }

        foreach (CoreProperty prop in PropertyRegistry.GetRegistered(obj.GetType()))
        {
            if (prop.PropertyType.IsValueType)
                continue;

            if (obj.GetValue(prop) is IFileSource fileSource)
                yield return fileSource;
        }
    }

    private static IEnumerable<IFileSource> EnumerateGraphSources(GraphModel model, HashSet<GraphGroup> visitedGraphGroups)
    {
        foreach (GraphNode node in model.Nodes)
        {
            // Every input port whose value is an IFileSource — VideoSourceNode.Source, ImageSourceNode.Source,
            // and a GroupNode's outer-boundary inputs alike. Gating on the value type (not a specific node
            // type) keeps this uniform across all source-carrying nodes.
            foreach (IFileSource source in EnumerateNodeInputSources(node))
                yield return source;

            // A user-constructed GroupNode can reference a GraphGroup that (transitively) contains the
            // same GroupNode, producing an infinite walk. The visited set makes the recursion terminate.
            if (node is GroupNode groupNode && visitedGraphGroups.Add(groupNode.Group))
            {
                foreach (IFileSource source in EnumerateGraphSources(groupNode.Group, visitedGraphGroups))
                    yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateNodeInputSources(GraphNode node)
    {
        foreach (INodeMember member in node.Items)
        {
            if (member is not IInputPort { Property: { } property })
                continue;

            if (property.GetValue() is IFileSource current)
                yield return current;

            if (property is IAnimatablePropertyAdapter { Animation: { } animation })
            {
                foreach (IFileSource source in EnumerateAnimatedFileSources(animation))
                    yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateFilterEffectGraphSources(
        FilterEffect? effect,
        HashSet<FilterEffectGroup> visitedFilterEffectGroups,
        HashSet<GraphGroup> visitedGraphGroups)
    {
        if (effect is FilterEffectGroup group && visitedFilterEffectGroups.Add(group))
        {
            foreach (FilterEffect child in group.Children)
            {
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(child, visitedFilterEffectGroups, visitedGraphGroups))
                    yield return source;
            }
        }
        else if (effect is NodeGraphFilterEffect { Model.CurrentValue: { } model })
        {
            foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups))
                yield return source;
        }
    }

    private static IEnumerable<IFileSource> EnumerateAnimatedFileSources(IAnimation? animation)
    {
        if (animation is not KeyFrameAnimation keyFrameAnimation)
            yield break;

        foreach (IKeyFrame keyFrame in keyFrameAnimation.KeyFrames)
        {
            if (keyFrame.Value is IFileSource source)
                yield return source;
        }
    }
}
