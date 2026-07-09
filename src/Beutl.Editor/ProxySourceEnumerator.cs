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
    /// Enumerates every <see cref="IFileSource"/> reachable from <paramref name="element"/> — the full
    /// media walk (not just the proxy-eligible video subset). Reaches direct/animated values, node-graph
    /// adapter inputs, filter-effect subgraphs, presenter/decorator targets, and referenced scenes.
    /// </summary>
    /// <param name="skipDisabledElements">
    /// When true, elements with <see cref="Element.IsEnabled"/> == false inside referenced scenes are
    /// skipped (they never render). Export preflight passes true; proxy generation / badge enumeration
    /// leaves it false so a disabled clip still contributes a proxy source.
    /// </param>
    public static IEnumerable<IFileSource> EnumerateFileSources(
        Element element, HashSet<Scene>? visitedScenes = null, bool skipDisabledElements = false)
    {
        ArgumentNullException.ThrowIfNull(element);
        return EnumerateElementFileSources(
            element, visitedScenes ?? new HashSet<Scene>(ReferenceEqualityComparer.Instance), skipDisabledElements);
    }

    /// <summary>
    /// Collects the file-system paths of every <see cref="IFileSource"/> referenced anywhere in
    /// <paramref name="root"/>, regardless of whether each file lives inside or outside the project
    /// directory. Covers the broad <see cref="IFileSource"/> property walk AND every source the
    /// property walk cannot reach — node-graph adapter inputs (a port is an <see cref="IPropertyAdapter"/>,
    /// not a plain <see cref="IProperty{T}"/> on an <see cref="EngineObject"/>), including those held
    /// inside referenced scenes. <c>SimpleProperty</c> attaches hierarchical property values (a
    /// referenced scene, a presenter target) as hierarchical children, so the hierarchy itself is
    /// user-cyclable and the walk is cycle-safe. Paths are deduped with
    /// <see cref="StringComparer.Ordinal"/>.
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

        // SimpleProperty attaches IHierarchical property values (a referenced scene, a presenter
        // target) as hierarchical children, so user-constructible reference cycles reach the
        // hierarchy itself; the unguarded EnumerateAllChildren recursion would overflow the stack.
        List<IHierarchical> allChildren = [.. EnumerateAllChildrenCycleSafe(root)];

        foreach (CoreObject obj in allChildren.OfType<CoreObject>())
            CollectFileSourcePaths(obj, paths, includeObjectUris);

        if (root is CoreObject rootObj)
            CollectFileSourcePaths(rootObj, paths, includeObjectUris);

        // The property walk above cannot see node-graph adapter inputs (a port is an IPropertyAdapter,
        // not an EngineObject property), including ports inside referenced scenes, so descend the
        // element walk here too. A shared visited-scene set resolves cross-element references to the
        // same scene once.
        var visitedScenes = new HashSet<Scene>(ReferenceEqualityComparer.Instance);
        foreach (Element element in allChildren.OfType<Element>())
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

    private static IEnumerable<IFileSource> EnumerateElementFileSources(
        Element element, HashSet<Scene> visitedScenes, bool skipDisabledElements = false)
    {
        foreach (EngineObject obj in element.Objects)
        {
            foreach (IFileSource source in EnumerateObjectFileSources(
                obj,
                visitedScenes,
                new HashSet<GraphGroup>(ReferenceEqualityComparer.Instance),
                new HashSet<FilterEffectGroup>(ReferenceEqualityComparer.Instance),
                new HashSet<Drawable>(ReferenceEqualityComparer.Instance),
                skipDisabledElements))
            {
                yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateObjectFileSources(
        EngineObject obj,
        HashSet<Scene> visitedScenes,
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<FilterEffectGroup> visitedFilterEffectGroups,
        HashSet<Drawable> visitedTargets,
        bool skipDisabledElements)
    {
        // Direct IFileSource-valued properties (current + animated): SourceVideo/SourceImage/SourceSound.
        foreach (IFileSource source in EnumeratePropertyFileSources(obj))
            yield return source;

        if (obj is Drawable drawable)
        {
            // A VideoSourceNode / ImageSourceNode can live inside a NodeGraphFilterEffect on any
            // drawable's filter chain; the render path evaluates those, so scan them too.
            foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                drawable.FilterEffect.CurrentValue,
                visitedFilterEffectGroups,
                visitedGraphGroups,
                new HashSet<FilterEffect>(ReferenceEqualityComparer.Instance)))
                yield return source;

            switch (drawable)
            {
                case NodeGraphDrawable { Model.CurrentValue: { } model }:
                    foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups))
                        yield return source;

                    break;

                case SceneDrawable { ReferencedScene.CurrentValue: { } referencedScene }:
                    foreach (IFileSource source in EnumerateReferencedSceneSources(referencedScene, visitedScenes, skipDisabledElements))
                        yield return source;

                    break;

                case DrawableGroup group:
                    foreach (Drawable child in group.Children)
                    {
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements))
                            yield return source;
                    }

                    break;

                // DrawableDecorator, DrawableTimeController, and DrawablePresenter are
                // IFlowOperator/IPresenter that render nested drawables the property walk cannot
                // reach, so a SourceVideo placed in them would otherwise be invisible to the Proxies
                // tab, badges, and cache invalidation. Target is a reference property (not
                // ownership), so target chains are user-cyclable — the visited set makes the
                // recursion terminate.
                case DrawableDecorator decorator:
                    foreach (Drawable child in decorator.Children)
                    {
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements))
                            yield return source;
                    }

                    break;

                case DrawableTimeController { Target.CurrentValue: { } target }:
                    if (visitedTargets.Add(target))
                    {
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            target, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements))
                            yield return source;
                    }

                    break;

                case DrawablePresenter { Target.CurrentValue: { } presented }:
                    if (visitedTargets.Add(presented))
                    {
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            presented, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements))
                            yield return source;
                    }

                    break;
            }
        }

        // A SceneSound is a Sound (not a Drawable), so it is not covered by the Drawable switch above;
        // its referenced scene still contributes renderable media and must be descended.
        if (obj is SceneSound { ReferencedScene.CurrentValue: { } soundScene })
        {
            foreach (IFileSource source in EnumerateReferencedSceneSources(soundScene, visitedScenes, skipDisabledElements))
                yield return source;
        }
    }

    private static IEnumerable<IHierarchical> EnumerateAllChildrenCycleSafe(IHierarchical root)
    {
        var visited = new HashSet<IHierarchical>(ReferenceEqualityComparer.Instance) { root };
        var stack = new Stack<IHierarchical>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            foreach (IHierarchical child in stack.Pop().HierarchicalChildren)
            {
                if (visited.Add(child))
                {
                    yield return child;
                    stack.Push(child);
                }
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateReferencedSceneSources(
        Scene scene, HashSet<Scene> visitedScenes, bool skipDisabledElements)
    {
        // Scene references are user-constructible and can cycle; the visited set makes the walk
        // terminate (render-time Enter/Exit is the only other guard).
        if (!visitedScenes.Add(scene))
            yield break;

        foreach (Element child in scene.Children)
        {
            // A disabled child never renders through SceneCompositor.SortLayers, so export preflight
            // must not demand its original file exist. Time-range filtering through the SceneDrawable's
            // remap is out of scope; only the render-independent IsEnabled gate is applied here.
            if (skipDisabledElements && !child.IsEnabled)
                continue;

            foreach (IFileSource source in EnumerateElementFileSources(child, visitedScenes, skipDisabledElements))
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
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<FilterEffect> visitedFilterEffects)
    {
        // Presenter/delay targets are reference properties, so a filter chain is user-cyclable;
        // the visited set makes the recursion terminate.
        if (effect is null || !visitedFilterEffects.Add(effect))
            yield break;

        switch (effect)
        {
            case FilterEffectGroup group when visitedFilterEffectGroups.Add(group):
                foreach (FilterEffect child in group.Children)
                {
                    foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                        child, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects))
                        yield return source;
                }

                break;

            case NodeGraphFilterEffect { Model.CurrentValue: { } model }:
                foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups))
                    yield return source;

                break;

            // FilterEffectPresenter and DelayAnimationEffect render a nested filter effect the
            // property walk cannot reach, so a NodeGraphFilterEffect source inside them would be
            // invisible to the Proxies tab, cache invalidation, and export preflight.
            case FilterEffectPresenter { Target.CurrentValue: { } presented }:
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    presented, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects))
                    yield return source;

                break;

            case DelayAnimationEffect { Effect.CurrentValue: { } delayed }:
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    delayed, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects))
                    yield return source;

                break;
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
