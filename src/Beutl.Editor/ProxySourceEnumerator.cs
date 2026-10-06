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
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

// Single source of truth for "which media does this reference?". The walk mirrors the render path,
// so it reaches every IFileSource a renderer can open: object properties, animated values, node-graph
// adapter inputs, filter-effect subgraphs, and referenced scenes. Proxy resolution (video-only) and
// export preflight (all media) both build on this one traversal.
public static partial class ProxySourceEnumerator
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
        TimeRange? localRange = null,
        TimeRange? sceneWindow = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        return EnumerateElementFileSources(
            element,
            visitedScenes ?? new HashSet<(Scene, CompositionTarget?)>(),
            skipDisabledElements,
            renderTarget,
            localRange,
            sceneWindow);
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
        // The broad asset walk retains authored layer values for later reconnection, even when
        // render/proxy discovery excludes an output with no accepted consumers.
        if (obj is LayerInputNode.ILayerInputPort { Property: { } property })
        {
            var visitedValues = new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);
            foreach (IFileSource source in EnumeratePropertyValueFileSources(property.GetValue(), null, false, visitedValues))
                AddFileSourcePath(source.Uri, paths);
            if (property is IAnimatablePropertyAdapter { Animation: { } animation })
            {
                foreach (IFileSource source in EnumerateAnimatedFileSources(animation, visitedValues: visitedValues))
                    AddFileSourcePath(source.Uri, paths);
            }
        }

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
        TimeRange? localRange = null,
        TimeRange? sceneWindow = null)
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
                new HashSet<Drawable>(ReferenceEqualityComparer.Instance),
                new HashSet<Drawable>(ReferenceEqualityComparer.Instance),
                skipDisabledElements,
                renderTarget,
                localRange,
                sceneWindow))
            {
                yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateObjectFileSources(
        EngineObject obj,
        HashSet<(Scene, CompositionTarget?)> visitedScenes,
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<Drawable> visitedTargets,
        HashSet<Drawable> visitedFullWalkTargets,
        bool skipDisabledElements,
        CompositionTarget? renderTarget,
        TimeRange? localRange = null,
        TimeRange? sceneWindow = null,
        IReadOnlySet<IProperty>? connectedNodeInputs = null)
    {
        // Direct IFileSource-valued properties (current + animated): SourceVideo/SourceImage/SourceSound.
        // Thread the walk context so a rendered structural value reachable only as a property (a
        // DrawableBrush's Drawable, a visualizer's SceneSound) dispatches through the guarded walks — from
        // the current value, an expression, an animation keyframe, or a node input alike.
        var walkContext = new ObjectWalkContext(
            visitedScenes, visitedGraphGroups, visitedTargets, visitedFullWalkTargets, renderTarget, sceneWindow)
        {
            ConnectedNodeInputs = connectedNodeInputs
        };
        foreach (IFileSource source in EnumeratePropertyFileSources(
            obj, localRange, skipDisabledElements, sceneWindow: sceneWindow, walkContext: walkContext))
            yield return source;

        if (obj is Drawable drawable)
        {
            // A VideoSourceNode / ImageSourceNode can live inside a NodeGraphFilterEffect on any
            // drawable's filter chain; the render path evaluates those, so scan them too. The render uses
            // the effective FilterEffect value, so resolve an expression-supplied one before walking.
            foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                ResolveExpressionValue<FilterEffect>(drawable, drawable.FilterEffect, walkContext: walkContext),
                visitedGraphGroups,
                new HashSet<FilterEffect>(ReferenceEqualityComparer.Instance),
                new HashSet<FilterEffect>(ReferenceEqualityComparer.Instance),
                skipDisabledElements,
                localRange,
                sceneWindow,
                walkContext))
                yield return source;

            switch (drawable)
            {
                case NodeGraphDrawable { Model.CurrentValue: { } model }:
                    foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups, localRange, sceneWindow: sceneWindow, walkContext: walkContext, skipDisabledElements: skipDisabledElements))
                        yield return source;

                    break;

                case SceneDrawable sceneDrawable
                    when ResolveExpressionValue<Scene>(sceneDrawable, sceneDrawable.ReferencedScene, walkContext: walkContext) is { } referencedScene:
                    // A SceneDrawable renders only the referenced scene's graphics, never its audio, so
                    // narrow the descent to Graphics regardless of the outer target: an audio-only
                    // original missing inside a graphically-embedded scene must not block a video export.
                    // It evaluates the referenced scene at (clock - sceneDrawable.Start); sceneDrawable.Start
                    // is element.Start (objects are time-anchored to their element), so the referenced scene
                    // runs in element-local time — the same space localRange is already expressed in. Thread
                    // localRange directly; subtracting sceneDrawable.Start here would double-shift by
                    // element.Start and drop in-window keyframes for non-zero-start elements.
                    // The referenced scene runs in element-local time, so its own scene-time clock (what
                    // its global-clock keyframes sample) equals this element's localRange — pass it as the
                    // inner scene window.
                    foreach (IFileSource source in EnumerateReferencedSceneSources(
                        referencedScene, visitedScenes, skipDisabledElements, CompositionTarget.Graphics,
                        localRange, localRange))
                        yield return source;

                    break;

                case DrawableGroup group:
                    foreach (Drawable child in group.Children)
                    {
                        // The nested render path (DrawableGroup.OnDraw -> DrawDrawable -> Drawable.Render)
                        // skips a disabled child, so preflight must not demand its file either.
                        if (skipDisabledElements && !child.IsEnabled)
                            continue;
                        if (connectedNodeInputs != null && !TryVisitGraphDrawable(child, localRange, walkContext))
                            continue;

                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedTargets, visitedFullWalkTargets, skipDisabledElements, renderTarget, localRange, sceneWindow, connectedNodeInputs))
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
                        if (connectedNodeInputs != null && !TryVisitGraphDrawable(child, localRange, walkContext))
                            continue;

                        // A decorator renders its children at the same composition time (it pushes only
                        // transform/opacity/effect, never remaps time), so the render window still maps
                        // directly — thread localRange through, unlike the time-remapping cases below.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedTargets, visitedFullWalkTargets, skipDisabledElements, renderTarget, localRange, sceneWindow, connectedNodeInputs))
                            yield return source;
                    }

                    break;

                case DrawableTimeController controller
                    when ResolveExpressionValue<Drawable>(controller, controller.Target, walkContext: walkContext) is { } target:
                    // The remapped full walk (range dropped below) is a superset of a window-preserving
                    // one, so it must run even if a presenter already window-visited this target — dedup it
                    // in its own set so that earlier windowed visit cannot suppress it (identity-only would).
                    if ((!skipDisabledElements || target.IsEnabled) && visitedFullWalkTargets.Add(target))
                    {
                        // A time controller remaps composition time, so neither the element-local window
                        // nor the scene-time window still maps — drop both to the conservative full walk.
                        // PostUpdate renders context.Get(Target), so resolve an expression-supplied one.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            target, visitedScenes, visitedGraphGroups, visitedTargets, visitedFullWalkTargets, skipDisabledElements, renderTarget, connectedNodeInputs: connectedNodeInputs))
                            yield return source;
                    }

                    break;

                case DrawablePresenter presenter
                    when ResolveExpressionValue<Drawable>(presenter, presenter.Target, walkContext: walkContext) is { } presented:
                    // A full walk already covers this windowed subset, so skip when the target was
                    // full-walked; otherwise dedup the windowed visit in visitedTargets.
                    if ((!skipDisabledElements || presented.IsEnabled)
                        && !visitedFullWalkTargets.Contains(presented)
                        && visitedTargets.Add(presented))
                    {
                        // A presenter forwards the same composition time to its target (no remap), so the
                        // render window maps directly — thread localRange through, unlike the time
                        // controller above. The render uses the effective Target, so resolve an
                        // expression-supplied one.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            presented, visitedScenes, visitedGraphGroups, visitedTargets, visitedFullWalkTargets, skipDisabledElements, renderTarget, localRange, sceneWindow, connectedNodeInputs))
                            yield return source;
                    }

                    break;
            }
        }

        // A SceneSound is a Sound (not a Drawable), so it is not covered by the Drawable switch above;
        // its referenced scene contributes only audio to the render, so narrow the descent to Audio. Its
        // audio graph applies Shift(OffsetPosition) then Speed before the referenced scene is sampled. For
        // the identity map (OffsetPosition == 0, Speed == 100, neither animated) those nodes are pass-
        // through, so the referenced scene sees the element-local window and localRange maps directly;
        // any real remap makes the window unexpressible, so fall back to the conservative full walk.
        if (obj is SceneSound sceneSound
            && ResolveExpressionValue<Scene>(sceneSound, sceneSound.ReferencedScene, walkContext: walkContext) is { } soundScene)
        {
            TimeRange? soundWindow = IsIdentityAudioMap(sceneSound) ? localRange : null;
            foreach (IFileSource source in EnumerateReferencedSceneSources(
                soundScene, visitedScenes, skipDisabledElements, CompositionTarget.Audio, soundWindow, soundWindow))
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
                    child, visitedScenes, visitedGraphGroups, visitedTargets, visitedFullWalkTargets, skipDisabledElements, renderTarget, localRange, sceneWindow, connectedNodeInputs))
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
        Scene scene, HashSet<(Scene, CompositionTarget?)> visitedScenes, bool skipDisabledElements, CompositionTarget? renderTarget,
        TimeRange? referencedSceneWindow = null, TimeRange? outerSceneWindow = null)
    {
        // Scene references are user-constructible and can cycle. Guard with a recursion STACK, not a
        // global visited set: dedup only scenes currently on the descent path (remove on exit) so a
        // reference cycle terminates, while the same scene reached again from a sibling embed — with its
        // own render window — is still traversed. A global set would suppress the second windowed visit
        // and miss a source only that window requires. Key by (scene, renderTarget) so a SceneDrawable
        // (Graphics) and a SceneSound (Audio) embed of one scene each preflight their own facet.
        (Scene, CompositionTarget?) key = (scene, renderTarget);
        if (!visitedScenes.Add(key))
            yield break;

        SceneLayerSkipModel layerSkip = SceneLayerSkipModel.Build(scene);
        try
        {
            foreach (Element child in scene.Children)
            {
                // A disabled child never renders through SceneCompositor.SortLayers, so export preflight
                // must not demand its original file exist; the render-independent IsEnabled gate applies.
                if (skipDisabledElements && !child.IsEnabled)
                    continue;

                // SortLayers also drops a muted / non-solo layer for the pass it composes, so the embedded
                // scene never reads that child's source for this target — preflight must skip it too. Only
                // in the export-preflight walk (skipDisabledElements), and only for a concrete target.
                if (skipDisabledElements && renderTarget is { } skipTarget && layerSkip.ShouldSkip(child.ZIndex, skipTarget))
                    continue;

                // SortLayers only composes a child whose range intersects the sampled time, so a child
                // entirely outside the referenced-scene window never renders — skip it before descending
                // (the windowed keyframe filter alone does not gate a direct, unanimated child source).
                if (skipDisabledElements && referencedSceneWindow is { } window && !RangeOverlapsWindow(child.Range, window))
                    continue;

                // referencedSceneWindow is in referenced-scene time; each child samples its own local
                // animations at scene-time - child.Start, while its global-clock keyframes sample the
                // referenced scene's own clock (outerSceneWindow, unshifted by child.Start).
                TimeRange? childWindow = referencedSceneWindow?.SubtractStart(child.Start);
                foreach (IFileSource source in EnumerateElementFileSources(child, visitedScenes, skipDisabledElements, renderTarget, childWindow, outerSceneWindow))
                    yield return source;
            }
        }
        finally
        {
            visitedScenes.Remove(key);
        }
    }

    // SortLayers point-samples with Contains(time), so a save-frame preflight's zero-duration window
    // (IsEmpty) still renders a child active at window.Start even though Intersects is false for an empty
    // range. Test point-containment for an empty window; the usual overlap otherwise.
    private static bool RangeOverlapsWindow(TimeRange range, TimeRange window)
        => window.IsEmpty ? range.Contains(window.Start) : range.Intersects(window);

    // Carries the object walk's guarded-navigator state into the property-value recursion so a rendered
    // structural value reachable only as a property (a DrawableBrush's Drawable, a visualizer's SceneSound)
    // can be dispatched through the same guarded walks the structural navigator uses.
    private sealed record ObjectWalkContext(
        HashSet<(Scene, CompositionTarget?)> VisitedScenes,
        HashSet<GraphGroup> VisitedGraphGroups,
        HashSet<Drawable> VisitedTargets,
        HashSet<Drawable> VisitedFullWalkTargets,
        CompositionTarget? RenderTarget,
        TimeRange? SceneWindow)
    {
        public IReadOnlySet<IProperty>? ConnectedNodeInputs { get; init; }
    }

    // The SceneSound audio graph is a pass-through (no time remap) only when Shift and Speed are both
    // identity: OffsetPosition is 0 with no animation, and Speed is 100 (== 1.0x) with no animation.
    // Then the referenced scene samples the element-local window directly.
    private static bool IsIdentityAudioMap(SceneSound sceneSound)
        => sceneSound.OffsetPosition.Animation is null
           && sceneSound.OffsetPosition.CurrentValue == TimeSpan.Zero
           && sceneSound.Speed.Animation is null
           && sceneSound.Speed.CurrentValue == 100f;
}
