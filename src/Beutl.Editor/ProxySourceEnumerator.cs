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
    /// <param name="skipDisabledElements">Skips disabled elements and objects, as the render path does.</param>
    /// <param name="renderTarget">Skips objects of another composition target, as the render path does.</param>
    /// <param name="localRange">
    /// The render window in the element's local time. It resets to null wherever nesting remaps time.
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
    /// Collects the file paths of every <see cref="IFileSource"/> under <paramref name="root"/>, including
    /// node-graph inputs and referenced scenes.
    /// </summary>
    public static IReadOnlySet<string> EnumerateFileSources(IHierarchical root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var paths = new HashSet<string>(StringComparer.Ordinal);

        // SimpleProperty attaches IHierarchical property values (a referenced scene, a presenter
        // target) as hierarchical children, so user-constructible reference cycles reach the
        // hierarchy itself; the unguarded EnumerateAllChildren recursion would overflow the stack.
        List<IHierarchical> allChildren = [.. EnumerateAllChildrenCycleSafe(root)];

        foreach (CoreObject obj in allChildren.OfType<CoreObject>())
            CollectFileSourcePaths(obj, paths);

        if (root is CoreObject rootObj)
            CollectFileSourcePaths(rootObj, paths);

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

    private static void CollectFileSourcePaths(CoreObject obj, HashSet<string> paths)
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
        TimeRange? sceneWindow = null,
        Rational? frameRate = null,
        MediaTime? frameOrigin = null)
    {
        foreach (EngineObject obj in element.Objects)
        {
            // Mirror Element.CollectObjects: the render path skips disabled objects and objects whose
            // composition target does not match, so the preflight must not demand their files either.
            if (skipDisabledElements && !obj.IsEnabled.Value)
                continue;

            if (renderTarget is { } target)
            {
                CompositionTarget objTarget = obj.GetCompositionTarget();
                if (objTarget != CompositionTarget.Unknown && objTarget != target)
                    continue;
            }

            var walkContext = new ObjectWalkContext(
                visitedScenes,
                new HashSet<GraphGroup>(ReferenceEqualityComparer.Instance),
                new HashSet<Drawable>(ReferenceEqualityComparer.Instance),
                new HashSet<Drawable>(ReferenceEqualityComparer.Instance),
                sceneWindow)
            {
                FrameRate = frameRate,
                FrameOrigin = frameOrigin
            };
            foreach (IFileSource source in EnumerateObjectFileSources(obj, walkContext, skipDisabledElements, localRange))
            {
                yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateObjectFileSources(
        EngineObject obj,
        ObjectWalkContext walkContext,
        bool skipDisabledElements,
        TimeRange? localRange = null)
    {
        // Direct IFileSource-valued properties (current + animated): SourceVideo/SourceImage/SourceSound.
        // Thread the walk context so a rendered structural value reachable only as a property (a
        // DrawableBrush's Drawable, a visualizer's SceneSound) dispatches through the guarded walks — from
        // the current value, an expression, an animation keyframe, or a node input alike.
        foreach (IFileSource source in EnumeratePropertyFileSources(
            obj, localRange, skipDisabledElements, sceneWindow: walkContext.SceneWindow, walkContext: walkContext))
            yield return source;

        if (obj is Drawable drawable)
        {
            // A VideoSourceNode / ImageSourceNode can live inside a NodeGraphFilterEffect on any
            // drawable's filter chain; the render path evaluates those, so scan them too. The render uses
            // the effective FilterEffect value, so resolve an expression-supplied one before walking.
            foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                ResolveExpressionValue<FilterEffect>(drawable, drawable.FilterEffect, walkContext: walkContext),
                walkContext.VisitedGraphGroups,
                new HashSet<FilterEffect>(ReferenceEqualityComparer.Instance),
                new HashSet<FilterEffect>(ReferenceEqualityComparer.Instance),
                skipDisabledElements,
                localRange,
                walkContext.SceneWindow,
                walkContext))
                yield return source;

            switch (drawable)
            {
                case NodeGraphDrawable { Model.Value: { } model }:
                    foreach (IFileSource source in EnumerateGraphSources(model, walkContext.VisitedGraphGroups, localRange, sceneWindow: walkContext.SceneWindow, walkContext: walkContext, skipDisabledElements: skipDisabledElements))
                        yield return source;

                    break;

                case SceneDrawable sceneDrawable
                    when ResolveExpressionValue<Scene>(sceneDrawable, sceneDrawable.ReferencedScene, walkContext: walkContext) is { } referencedScene:
                    // Match SceneDrawable's offset into the referenced scene for both the window and
                    // the output frame grid. A runtime offset needs the full walk, not its stored value.
                    MediaTime? graphicsShift = !sceneDrawable.OffsetPosition.HasExpression
                        && sceneDrawable.OffsetPosition.GetKeyFrameAnimation() is null
                        && walkContext.ConnectedNodeInputs?.Contains(sceneDrawable.OffsetPosition) != true
                            ? referencedScene.Start.Value + sceneDrawable.OffsetPosition.Value : null;
                    TimeRange? graphicsWindow = graphicsShift is { } shift ? localRange?.AddStart(shift) : null;
                    foreach (IFileSource source in EnumerateReferencedSceneSources(
                        referencedScene, walkContext.VisitedScenes, skipDisabledElements, CompositionTarget.Graphics,
                        graphicsWindow, graphicsWindow, walkContext.FrameRate,
                        walkContext.FrameOrigin - sceneDrawable.Start + graphicsShift))
                        yield return source;

                    break;

                case DrawableGroup group:
                    // The nested render path (DrawableGroup.OnDraw -> DrawDrawable -> Drawable.Render)
                    // skips a disabled child, so preflight must not demand its file either.
                    foreach (IFileSource source in EnumerateChildDrawableSources(
                        group.Children, walkContext, skipDisabledElements, localRange))
                        yield return source;

                    break;

                // Decorators, time controllers and presenters render nested drawables the property walk
                // cannot reach. Targets are references and can form cycles, so the visited sets stop
                // the recursion; disabled ones are skipped as the render path skips them.
                case DrawableDecorator decorator:
                    // A decorator renders its children at the same composition time (it pushes only
                    // transform/opacity/effect, never remaps time), so the render window still maps
                    // directly — thread localRange through, unlike the time-remapping cases below.
                    foreach (IFileSource source in EnumerateChildDrawableSources(
                        decorator.Children, walkContext, skipDisabledElements, localRange))
                        yield return source;

                    break;

                case DrawableTimeController controller
                    when ResolveExpressionValue<Drawable>(controller, controller.Target, walkContext: walkContext) is { } target:
                    // The remapped full walk (range dropped below) is a superset of a window-preserving
                    // one, so it must run even if a presenter already window-visited this target — dedup it
                    // in its own set so that earlier windowed visit cannot suppress it (identity-only would).
                    if ((!skipDisabledElements || target.IsEnabled.Value) && walkContext.VisitedFullWalkTargets.Add(target))
                    {
                        // A time controller remaps composition time, so neither the element-local window
                        // nor the scene-time window still maps — drop both to the conservative full walk.
                        // PostReconcile renders context.Get(Target), so resolve an expression-supplied one.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            target, walkContext with { SceneWindow = null, FrameOrigin = null }, skipDisabledElements))
                            yield return source;
                    }

                    break;

                case DrawablePresenter presenter
                    when ResolveExpressionValue<Drawable>(presenter, presenter.Target, walkContext: walkContext) is { } presented:
                    // A full walk already covers this windowed subset, so skip when the target was
                    // full-walked; otherwise dedup the windowed visit in VisitedTargets.
                    if ((!skipDisabledElements || presented.IsEnabled.Value)
                        && !walkContext.VisitedFullWalkTargets.Contains(presented)
                        && walkContext.VisitedTargets.Add(presented))
                    {
                        // A presenter does not remap time, so localRange maps directly. The render uses
                        // the effective Target, so resolve an expression-supplied one.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            presented, walkContext, skipDisabledElements, localRange))
                            yield return source;
                    }

                    break;
            }
        }

        // A SceneSound's referenced scene contributes only audio, so narrow to Audio. localRange maps
        // directly only when its offset and speed are identity; any other remap falls back to the full walk.
        if (obj is SceneSound sceneSound
            && ResolveExpressionValue<Scene>(sceneSound, sceneSound.ReferencedScene, walkContext: walkContext) is { } soundScene)
        {
            // Shifted into referenced-scene time, like the embed composes it.
            TimeRange? soundWindow = IsIdentityAudioMap(sceneSound) ? localRange?.AddStart(soundScene.Start.Value) : null;
            foreach (IFileSource source in EnumerateReferencedSceneSources(
                soundScene, walkContext.VisitedScenes, skipDisabledElements, CompositionTarget.Audio, soundWindow, soundWindow,
                walkContext.FrameRate, walkContext.FrameOrigin - sceneSound.Start + soundScene.Start.Value))
                yield return source;
        }

        // A SoundGroup renders its child Sounds through SoundGroup.Compose (the audio analogue of a
        // DrawableGroup), so a SourceSound/SceneSound nested in one is only reachable by walking the
        // group's Children; the property walk above cannot see them.
        if (obj is SoundGroup soundGroup)
        {
            foreach (Sound child in soundGroup.Children)
            {
                if (skipDisabledElements && !child.IsEnabled.Value)
                    continue;

                foreach (IFileSource source in EnumerateObjectFileSources(
                    child, walkContext, skipDisabledElements, localRange))
                    yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateChildDrawableSources(
        IListProperty<Drawable> children, ObjectWalkContext walkContext, bool skipDisabledElements, TimeRange? localRange)
    {
        foreach (Drawable child in children)
        {
            if (skipDisabledElements && !child.IsEnabled.Value)
                continue;
            if (walkContext.ConnectedNodeInputs != null && !TryVisitGraphDrawable(child, localRange, walkContext))
                continue;

            foreach (IFileSource source in EnumerateObjectFileSources(child, walkContext, skipDisabledElements, localRange))
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
        Scene scene, HashSet<(Scene, CompositionTarget?)> visitedScenes, bool skipDisabledElements, CompositionTarget? renderTarget,
        TimeRange? referencedSceneWindow = null, TimeRange? outerSceneWindow = null,
        Rational? frameRate = null, MediaTime? frameOrigin = null)
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

        try
        {
            if (skipDisabledElements && renderTarget is { } target)
            {
                foreach (IFileSource source in EnumerateRenderableSceneSources(
                             scene, visitedScenes, target, referencedSceneWindow, frameRate, frameOrigin))
                    yield return source;
                yield break;
            }

            foreach (Element child in scene.Children)
            {
                // A disabled child never renders through SceneCompositor.SortLayers, so export preflight
                // must not demand its original file exist; the render-independent IsEnabled gate applies.
                if (skipDisabledElements && !child.IsEnabled.Value)
                    continue;

                // SortLayers only composes a child whose range intersects the sampled time, so a child
                // entirely outside the referenced-scene window never renders — skip it before descending
                // (the windowed keyframe filter alone does not gate a direct, unanimated child source).
                if (skipDisabledElements && referencedSceneWindow is { } window && !RangeOverlapsWindow(child.Range, window))
                    continue;

                // referencedSceneWindow is in referenced-scene time; each child samples its own local
                // animations at scene-time - child.Start, while its global-clock keyframes sample the
                // referenced scene's own clock (outerSceneWindow, unshifted by child.Start).
                TimeRange? childWindow = referencedSceneWindow?.SubtractStart(child.Start.Value);
                foreach (IFileSource source in EnumerateElementFileSources(child, visitedScenes, skipDisabledElements, renderTarget,
                             childWindow, outerSceneWindow, frameRate, frameOrigin))
                    yield return source;
            }
        }
        finally
        {
            visitedScenes.Remove(key);
        }
    }

    // The sources of scene's elements that render to target in window, skipping what the render path skips; each
    // element is walked in its own active window.
    internal static IEnumerable<IFileSource> EnumerateRenderableSceneSources(
        Scene scene, HashSet<(Scene, CompositionTarget?)> visitedScenes, CompositionTarget target,
        TimeRange? window, Rational? frameRate, MediaTime? frameOrigin)
    {
        foreach ((Element child, TimeRange? active, MediaTime? origin) in SceneLayerSkipModel.EnumerateRenderableElements(
                     scene, target, window, frameRate, frameOrigin))
        {
            foreach (IFileSource source in EnumerateElementFileSources(child, visitedScenes, true, target,
                         active?.SubtractStart(child.Start.Value), active, frameRate, origin))
                yield return source;
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
        TimeRange? SceneWindow)
    {
        public IReadOnlySet<IProperty>? ConnectedNodeInputs { get; init; }
        public Rational? FrameRate { get; init; }
        public MediaTime? FrameOrigin { get; init; }
    }

    // The SceneSound audio graph is a pass-through (no time remap) only when Shift and Speed are both
    // identity: OffsetPosition is 0 with no animation, and Speed is 100 (== 1.0x) with no animation.
    // Then the referenced scene samples the element-local window directly.
    private static bool IsIdentityAudioMap(SceneSound sceneSound)
        => sceneSound.OffsetPosition.GetKeyFrameAnimation() is null
           && sceneSound.OffsetPosition.Value == MediaTime.Zero
           && sceneSound.Speed.GetKeyFrameAnimation() is null
           && sceneSound.Speed.Value == 100f;
}
