using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.IO;
using Beutl.Media;
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
    public static IEnumerable<VideoSource> EnumerateVideoSources(Element element, HashSet<(Scene, CompositionTarget?)>? visitedScenes = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return EnumerateElementFileSources(element, visitedScenes ?? new HashSet<(Scene, CompositionTarget?)>())
            .OfType<VideoSource>();
    }

    /// <summary>
    /// Enumerates every <see cref="IFileSource"/> reachable from <paramref name="element"/> — the full
    /// media walk (not just the proxy-eligible video subset). Reaches direct/animated values, node-graph
    /// adapter inputs, filter-effect subgraphs, presenter/decorator targets, and referenced scenes.
    /// </summary>
    /// <param name="skipDisabledElements">
    /// When true, disabled elements (referenced-scene children) and disabled objects are skipped, as
    /// the render path does via <c>Element.CollectObjects</c>. Export preflight passes true; proxy
    /// generation / badge enumeration leaves it false so a disabled clip still contributes a source.
    /// </param>
    /// <param name="renderTarget">
    /// When set, objects whose <see cref="EngineObject.GetCompositionTarget"/> is a different, non-Unknown
    /// target are skipped (again mirroring <c>Element.CollectObjects</c>). Save-frame passes
    /// <see cref="CompositionTarget.Graphics"/> so a missing audio original does not block a still image;
    /// full export leaves it null so both graphics and audio sources are required.
    /// </param>
    /// <param name="localRange">
    /// When set, animated <see cref="IFileSource"/> keyframes whose governing span falls entirely
    /// outside this render window (expressed in the element's local time) are dropped, so a
    /// since-replaced source referenced only by an out-of-window keyframe does not block a save-frame
    /// or partial-range export. Left null by callers with no render window (proxy scan / badge
    /// enumeration) and reset to null wherever descent crosses a time remap (referenced scenes, time
    /// controllers, presenters, node graphs), which keeps those paths at the conservative full walk.
    /// </param>
    public static IEnumerable<IFileSource> EnumerateFileSources(
        Element element,
        HashSet<(Scene, CompositionTarget?)>? visitedScenes = null,
        bool skipDisabledElements = false,
        CompositionTarget? renderTarget = null,
        TimeRange? localRange = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return EnumerateElementFileSources(
            element,
            visitedScenes ?? new HashSet<(Scene, CompositionTarget?)>(),
            skipDisabledElements,
            renderTarget,
            localRange);
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
        var visitedScenes = new HashSet<(Scene, CompositionTarget?)>();
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
        Element element,
        HashSet<(Scene, CompositionTarget?)> visitedScenes,
        bool skipDisabledElements = false,
        CompositionTarget? renderTarget = null,
        TimeRange? localRange = null)
    {
        foreach (EngineObject obj in element.Objects)
        {
            // Mirror Element.CollectObjects: the render path skips disabled objects and objects whose
            // composition target does not match, so the preflight must not demand their files either.
            if (skipDisabledElements && !obj.IsEnabled)
                continue;

            if (renderTarget is { } target)
            {
                CompositionTarget objTarget = obj.GetCompositionTarget();
                if (objTarget != CompositionTarget.Unknown && objTarget != target)
                    continue;
            }

            foreach (IFileSource source in EnumerateObjectFileSources(
                obj,
                visitedScenes,
                new HashSet<GraphGroup>(ReferenceEqualityComparer.Instance),
                new HashSet<FilterEffectGroup>(ReferenceEqualityComparer.Instance),
                new HashSet<Drawable>(ReferenceEqualityComparer.Instance),
                skipDisabledElements,
                renderTarget,
                localRange))
            {
                yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateObjectFileSources(
        EngineObject obj,
        HashSet<(Scene, CompositionTarget?)> visitedScenes,
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<FilterEffectGroup> visitedFilterEffectGroups,
        HashSet<Drawable> visitedTargets,
        bool skipDisabledElements,
        CompositionTarget? renderTarget,
        TimeRange? localRange = null)
    {
        // Direct IFileSource-valued properties (current + animated): SourceVideo/SourceImage/SourceSound.
        foreach (IFileSource source in EnumeratePropertyFileSources(obj, localRange))
            yield return source;

        if (obj is Drawable drawable)
        {
            // A VideoSourceNode / ImageSourceNode can live inside a NodeGraphFilterEffect on any
            // drawable's filter chain; the render path evaluates those, so scan them too.
            foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                drawable.FilterEffect.CurrentValue,
                visitedFilterEffectGroups,
                visitedGraphGroups,
                new HashSet<FilterEffect>(ReferenceEqualityComparer.Instance),
                skipDisabledElements,
                localRange))
                yield return source;

            switch (drawable)
            {
                case NodeGraphDrawable { Model.CurrentValue: { } model }:
                    foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups, localRange))
                        yield return source;

                    break;

                case SceneDrawable { ReferencedScene.CurrentValue: { } referencedScene }:
                    // A SceneDrawable renders only the referenced scene's graphics, never its audio, so
                    // narrow the descent to Graphics regardless of the outer target: an audio-only
                    // original missing inside a graphically-embedded scene must not block a video export.
                    foreach (IFileSource source in EnumerateReferencedSceneSources(referencedScene, visitedScenes, skipDisabledElements, CompositionTarget.Graphics))
                        yield return source;

                    break;

                case DrawableGroup group:
                    foreach (Drawable child in group.Children)
                    {
                        // The nested render path (DrawableGroup.OnDraw -> DrawDrawable -> Drawable.Render)
                        // skips a disabled child, so preflight must not demand its file either.
                        if (skipDisabledElements && !child.IsEnabled)
                            continue;

                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget, localRange))
                            yield return source;
                    }

                    break;

                // DrawableDecorator, DrawableTimeController, and DrawablePresenter are
                // IFlowOperator/IPresenter that render nested drawables the property walk cannot
                // reach, so a SourceVideo placed in them would otherwise be invisible to the Proxies
                // tab, badges, and cache invalidation. Target is a reference property (not
                // ownership), so target chains are user-cyclable — the visited set makes the
                // recursion terminate. A disabled nested drawable is skipped by the render path, so the
                // same skipDisabledElements gate applies before descending.
                case DrawableDecorator decorator:
                    foreach (Drawable child in decorator.Children)
                    {
                        if (skipDisabledElements && !child.IsEnabled)
                            continue;

                        // A decorator renders its children at the same composition time (it pushes only
                        // transform/opacity/effect, never remaps time), so the render window still maps
                        // directly — thread localRange through, unlike the time-remapping cases below.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget, localRange))
                            yield return source;
                    }

                    break;

                case DrawableTimeController { Target.CurrentValue: { } target }:
                    if ((!skipDisabledElements || target.IsEnabled) && visitedTargets.Add(target))
                    {
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            target, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget))
                            yield return source;
                    }

                    break;

                case DrawablePresenter { Target.CurrentValue: { } presented }:
                    if ((!skipDisabledElements || presented.IsEnabled) && visitedTargets.Add(presented))
                    {
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            presented, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget))
                            yield return source;
                    }

                    break;
            }
        }

        // A SceneSound is a Sound (not a Drawable), so it is not covered by the Drawable switch above;
        // its referenced scene contributes only audio to the render, so narrow the descent to Audio.
        if (obj is SceneSound { ReferencedScene.CurrentValue: { } soundScene })
        {
            foreach (IFileSource source in EnumerateReferencedSceneSources(soundScene, visitedScenes, skipDisabledElements, CompositionTarget.Audio))
                yield return source;
        }

        // A SoundGroup renders its child Sounds through SoundGroup.Compose (the audio analogue of a
        // DrawableGroup), so a SourceSound/SceneSound nested in one is only reachable by walking the
        // group's Children; the property walk above cannot see them.
        if (obj is SoundGroup soundGroup)
        {
            foreach (Sound child in soundGroup.Children)
            {
                if (skipDisabledElements && !child.IsEnabled)
                    continue;

                foreach (IFileSource source in EnumerateObjectFileSources(
                    child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget, localRange))
                    yield return source;
            }
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
        Scene scene, HashSet<(Scene, CompositionTarget?)> visitedScenes, bool skipDisabledElements, CompositionTarget? renderTarget)
    {
        // Scene references are user-constructible and can cycle; the visited set makes the walk
        // terminate (render-time Enter/Exit is the only other guard). Key by (scene, renderTarget): the
        // same scene can be reached once as a SceneDrawable (Graphics) and once as a SceneSound (Audio),
        // and each facet's media must be preflighted, so a Graphics visit must not suppress the Audio one.
        if (!visitedScenes.Add((scene, renderTarget)))
            yield break;

        foreach (Element child in scene.Children)
        {
            // A disabled child never renders through SceneCompositor.SortLayers, so export preflight
            // must not demand its original file exist. Time-range filtering through the SceneDrawable's
            // remap is out of scope; only the render-independent IsEnabled gate is applied here.
            if (skipDisabledElements && !child.IsEnabled)
                continue;

            foreach (IFileSource source in EnumerateElementFileSources(child, visitedScenes, skipDisabledElements, renderTarget))
                yield return source;
        }
    }

    private static IEnumerable<IFileSource> EnumeratePropertyFileSources(EngineObject obj, TimeRange? localRange = null)
    {
        foreach (IProperty property in obj.Properties)
        {
            // A property animated by >=1 keyframe is sampled from its animation, never its base
            // CurrentValue, so when range-filtering to a render window the base must not block export —
            // its file is never opened. Without a window (proxy/badge scan) keep the base too.
            bool baseOverridden = localRange is not null && AnimationSuppliesValue(property.Animation);
            if (!baseOverridden && property.CurrentValue is IFileSource fileSource)
                yield return fileSource;

            // Rendering (and the proxy scanner) consume animated file-source values too, so media
            // referenced only from keyframes must be enumerated as well.
            foreach (IFileSource animated in EnumerateAnimatedFileSources(property.Animation, localRange))
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

    private static IEnumerable<IFileSource> EnumerateGraphSources(
        GraphModel model, HashSet<GraphGroup> visitedGraphGroups, TimeRange? localRange = null)
    {
        foreach (GraphNode node in model.Nodes)
        {
            // Every input port whose value is an IFileSource — VideoSourceNode.Source, ImageSourceNode.Source,
            // and a GroupNode's outer-boundary inputs alike. Gating on the value type (not a specific node
            // type) keeps this uniform across all source-carrying nodes.
            foreach (IFileSource source in EnumerateNodeInputSources(node, localRange))
                yield return source;

            // A user-constructed GroupNode can reference a GraphGroup that (transitively) contains the
            // same GroupNode, producing an infinite walk. The visited set makes the recursion terminate.
            if (node is GroupNode groupNode && visitedGraphGroups.Add(groupNode.Group))
            {
                foreach (IFileSource source in EnumerateGraphSources(groupNode.Group, visitedGraphGroups, localRange))
                    yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateNodeInputSources(GraphNode node, TimeRange? localRange = null)
    {
        foreach (INodeMember member in node.Items)
        {
            if (member is not IInputPort { Property: { } property })
                continue;

            IAnimation? animation = (property as IAnimatablePropertyAdapter)?.Animation;

            // Like the EngineObject property walk: when range-filtering, the render samples an input's
            // animation, not its base value, so an overridden stale base must not block export.
            bool baseOverridden = localRange is not null && AnimationSuppliesValue(animation);
            if (!baseOverridden && property.GetValue() is IFileSource current)
                yield return current;

            if (animation is not null)
            {
                foreach (IFileSource source in EnumerateAnimatedFileSources(animation, localRange))
                    yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateFilterEffectGraphSources(
        FilterEffect? effect,
        HashSet<FilterEffectGroup> visitedFilterEffectGroups,
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<FilterEffect> visitedFilterEffects,
        bool skipDisabledElements,
        TimeRange? localRange = null)
    {
        // Presenter/delay targets are reference properties, so a filter chain is user-cyclable;
        // the visited set makes the recursion terminate.
        if (effect is null || !visitedFilterEffects.Add(effect))
            yield break;

        // FilterEffectRenderNode returns its input unchanged for a disabled effect, so a source inside
        // a disabled filter (or filter group) never renders; export preflight must not demand its file.
        if (skipDisabledElements && !effect.IsEnabled)
            yield break;

        switch (effect)
        {
            case FilterEffectGroup group when visitedFilterEffectGroups.Add(group):
                foreach (FilterEffect child in group.Children)
                {
                    foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                        child, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects, skipDisabledElements, localRange))
                        yield return source;
                }

                break;

            case NodeGraphFilterEffect { Model.CurrentValue: { } model }:
                foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups, localRange))
                    yield return source;

                break;

            // FilterEffectPresenter and DelayAnimationEffect render a nested filter effect the
            // property walk cannot reach, so a NodeGraphFilterEffect source inside them would be
            // invisible to the Proxies tab, cache invalidation, and export preflight.
            case FilterEffectPresenter { Target.CurrentValue: { } presented }:
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    presented, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects, skipDisabledElements))
                    yield return source;

                break;

            case DelayAnimationEffect { Effect.CurrentValue: { } delayed }:
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    delayed, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects, skipDisabledElements))
                    yield return source;

                break;
        }
    }

    private static bool AnimationSuppliesValue(IAnimation? animation)
        => animation is KeyFrameAnimation { KeyFrames.Count: > 0 };

    private static IEnumerable<IFileSource> EnumerateAnimatedFileSources(IAnimation? animation, TimeRange? localRange = null)
    {
        if (animation is not KeyFrameAnimation keyFrameAnimation)
            yield break;

        // Global-clock keyframes use scene time, but localRange is expressed in the element's local
        // time, so range-filtering them with a local window would drop keyframes that are actually
        // sampled. Fall back to the broad walk (every keyframe) for that case.
        TimeRange? window = keyFrameAnimation.UseGlobalClock ? null : localRange;

        IReadOnlyList<IKeyFrame> keyFrames = keyFrameAnimation.KeyFrames;
        for (int i = 0; i < keyFrames.Count; i++)
        {
            if (keyFrames[i].Value is not IFileSource source)
                continue;

            // With no render window every keyframe value counts (proxy scan / badge enumeration). With
            // one, keep a keyframe only if a sample inside the window could resolve to it: its value
            // governs the sorted span [previous keyframe, next keyframe), so drop it only when that
            // span lies wholly outside the window. The render window is half-open [Start, End) and the
            // next keyframe takes over at its exact key time, so the span end and range end are both
            // exclusive — use <= so a keyframe governing [.., Start) after a switch at Start is dropped.
            if (window is { } range)
            {
                TimeSpan spanStart = i > 0 ? keyFrames[i - 1].KeyTime : TimeSpan.MinValue;
                TimeSpan spanEnd = i < keyFrames.Count - 1 ? keyFrames[i + 1].KeyTime : TimeSpan.MaxValue;
                if (spanEnd <= range.Start || range.End <= spanStart)
                    continue;
            }

            yield return source;
        }
    }
}
