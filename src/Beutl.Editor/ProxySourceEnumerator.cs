using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
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
                new HashSet<FilterEffectGroup>(ReferenceEqualityComparer.Instance),
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
        HashSet<FilterEffectGroup> visitedFilterEffectGroups,
        HashSet<Drawable> visitedTargets,
        bool skipDisabledElements,
        CompositionTarget? renderTarget,
        TimeRange? localRange = null,
        TimeRange? sceneWindow = null)
    {
        // Direct IFileSource-valued properties (current + animated): SourceVideo/SourceImage/SourceSound.
        foreach (IFileSource source in EnumeratePropertyFileSources(obj, localRange, skipDisabledElements, sceneWindow: sceneWindow))
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
                localRange,
                sceneWindow))
                yield return source;

            switch (drawable)
            {
                case NodeGraphDrawable { Model.CurrentValue: { } model }:
                    foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups, localRange, sceneWindow: sceneWindow))
                        yield return source;

                    break;

                case SceneDrawable { ReferencedScene.CurrentValue: { } referencedScene }:
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

                        foreach (IFileSource source in EnumerateObjectFileSources(
                            child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget, localRange, sceneWindow))
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
                            child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget, localRange, sceneWindow))
                            yield return source;
                    }

                    break;

                case DrawableTimeController { Target.CurrentValue: { } target }:
                    if ((!skipDisabledElements || target.IsEnabled) && visitedTargets.Add(target))
                    {
                        // A time controller remaps composition time, so neither the element-local window
                        // nor the scene-time window still maps — drop both to the conservative full walk.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            target, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget))
                            yield return source;
                    }

                    break;

                case DrawablePresenter { Target.CurrentValue: { } presented }:
                    if ((!skipDisabledElements || presented.IsEnabled) && visitedTargets.Add(presented))
                    {
                        // A presenter forwards the same composition time to its target (no remap), so the
                        // render window maps directly — thread localRange through, unlike the time
                        // controller above.
                        foreach (IFileSource source in EnumerateObjectFileSources(
                            presented, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget, localRange, sceneWindow))
                            yield return source;
                    }

                    break;
            }
        }

        // A SceneSound is a Sound (not a Drawable), so it is not covered by the Drawable switch above;
        // its referenced scene contributes only audio to the render, so narrow the descent to Audio. Its
        // audio graph (ClipNode, then Shift(OffsetPosition) and Speed) remaps element-local time before
        // the referenced scene is sampled, so localRange cannot express the sampled window; drop to the
        // conservative full walk (null window) like the other time-remapping descents.
        if (obj is SceneSound { ReferencedScene.CurrentValue: { } soundScene })
        {
            foreach (IFileSource source in EnumerateReferencedSceneSources(
                soundScene, visitedScenes, skipDisabledElements, CompositionTarget.Audio))
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
                    child, visitedScenes, visitedGraphGroups, visitedFilterEffectGroups, visitedTargets, skipDisabledElements, renderTarget, localRange, sceneWindow))
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

        try
        {
            foreach (Element child in scene.Children)
            {
                // A disabled child never renders through SceneCompositor.SortLayers, so export preflight
                // must not demand its original file exist; the render-independent IsEnabled gate applies.
                if (skipDisabledElements && !child.IsEnabled)
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

    private static IEnumerable<IFileSource> EnumeratePropertyFileSources(
        EngineObject obj, TimeRange? localRange = null, bool skipDisabledElements = false, HashSet<EngineObject>? visitedValues = null,
        TimeRange? sceneWindow = null)
    {
        visitedValues ??= new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);

        foreach (IProperty property in obj.Properties)
        {
            // IProperty.GetValue evaluates an expression ahead of the animation/current value, so a
            // reference-expression pointing at a file source is what the render opens. Resolve it (no
            // evaluation — just id/path lookup) and report its sources. StringExpressions are arbitrary
            // C# and are not evaluated here.
            foreach (IFileSource source in EnumerateExpressionFileSources(obj, property.Expression, localRange, skipDisabledElements, visitedValues))
                yield return source;

            // When an expression is present, GetValue takes the expression branch and never samples the
            // base value or the animation, so in a windowed pass (which mirrors what the render opens)
            // they must not block export. Without a window (proxy/badge scan) keep them, since an
            // unresolvable expression is not something the scanner can follow.
            bool expressionOverrides = localRange is not null && property.Expression is not null;

            // A property animated by >=1 keyframe is sampled from its animation, never its base
            // CurrentValue, so when range-filtering to a render window the base must not block export —
            // its file is never opened. Without a window (proxy/badge scan) keep the base too.
            bool baseOverridden = localRange is not null && (expressionOverrides || AnimationSuppliesValue(property.Animation));
            if (!baseOverridden)
            {
                foreach (IFileSource source in EnumeratePropertyValueFileSources(property.CurrentValue, localRange, skipDisabledElements, visitedValues))
                    yield return source;
            }

            // Rendering (and the proxy scanner) consume animated file-source values too, so media
            // referenced only from keyframes must be enumerated as well — unless an expression overrides
            // the animation entirely in a windowed pass.
            if (!expressionOverrides)
            {
                foreach (IFileSource animated in EnumerateAnimatedFileSources(property.Animation, localRange, skipDisabledElements, visitedValues, sceneWindow))
                    yield return animated;
            }
        }

        foreach (CoreProperty prop in PropertyRegistry.GetRegistered(obj.GetType()))
        {
            if (prop.PropertyType.IsValueType)
                continue;

            foreach (IFileSource source in EnumeratePropertyValueFileSources(obj.GetValue(prop), localRange, skipDisabledElements, visitedValues))
                yield return source;
        }
    }

    // A property value that is itself an IFileSource is the direct case; a value that is an EngineObject
    // (an ImageBrush holding ImageBrush.Source, a LutEffect holding LutEffect.Source, an
    // AudioVisualizerDrawable holding a SourceSound, …) holds rendered file sources on its own properties,
    // so recurse. Only the structural navigators with dedicated guarded walks are NOT recursed here —
    // drawables, referenced-scene sounds (SceneSound), scenes, elements, graph nodes — since re-walking
    // them through the property graph would bypass their disabled/target/window guards. A plain value
    // Sound (a visualizer's Source) has no such walk, so it must be recursed.
    private static IEnumerable<IFileSource> EnumeratePropertyValueFileSources(
        object? value, TimeRange? localRange, bool skipDisabledElements, HashSet<EngineObject> visitedValues)
    {
        if (value is IFileSource fileSource)
        {
            yield return fileSource;
            yield break;
        }

        // A SoundGroup held as a property value (AudioVisualizerDrawable.Source) is composed by the
        // graphics render, but the object walk's `obj is SoundGroup` branch never fires here (the
        // enumerated object is the visualizer, not the group), so recurse its child Sounds directly,
        // honouring the same IsEnabled gate the audio compose applies.
        if (value is SoundGroup soundGroup)
        {
            if (!visitedValues.Add(soundGroup))
                yield break;

            foreach (Sound child in soundGroup.Children)
            {
                if (skipDisabledElements && !child.IsEnabled)
                    continue;

                foreach (IFileSource source in EnumeratePropertyValueFileSources(child, localRange, skipDisabledElements, visitedValues))
                    yield return source;
            }

            yield break;
        }

        if (value is not EngineObject engineObject
            || value is Drawable or SceneSound or Scene or Element or GraphNode
            || (skipDisabledElements && !engineObject.IsEnabled)
            || !visitedValues.Add(engineObject))
        {
            yield break;
        }

        foreach (IFileSource source in EnumeratePropertyFileSources(engineObject, localRange, skipDisabledElements, visitedValues))
            yield return source;
    }

    // A reference-expression resolves to another object's value (or one of its properties) by id; the
    // render opens whatever file source that yields. Resolve via the shared hierarchy root and route the
    // target through the value recursion. A cross-referenced target has its own guarded walk when it is
    // reached structurally, so recursing its resolved value here (guarded by visitedValues) only adds the
    // file sources it exposes as a value, without re-walking a scene/element/drawable navigator.
    private static IEnumerable<IFileSource> EnumerateExpressionFileSources(
        EngineObject owner, IExpression? expression, TimeRange? localRange, bool skipDisabledElements, HashSet<EngineObject> visitedValues)
    {
        if (expression is not IReferenceExpression reference
            || owner.FindHierarchicalRoot() is not ICoreObject root
            || root.FindById(reference.ObjectId) is not EngineObject target)
        {
            yield break;
        }

        if (!reference.HasPropertyPath)
        {
            foreach (IFileSource source in EnumeratePropertyValueFileSources(target, localRange, skipDisabledElements, visitedValues))
                yield return source;

            yield break;
        }

        // "GUID.PropertyName" resolves through PropertyLookup.TryGetPropertyValue, which tries the
        // target's IProperty surface first, then a registered CoreProperty. Mirror that resolution and,
        // for an IProperty, enumerate the same way the property walk does (its own expression, animated
        // keyframes, and current value) so the reference reaches sources hidden in the target's
        // animation/expression, not just its current value.
        IProperty? property = target.Properties.FirstOrDefault(
            p => string.Equals(p.Name, reference.PropertyPath, StringComparison.OrdinalIgnoreCase));
        if (property is not null)
        {
            foreach (IFileSource source in EnumerateExpressionFileSources(target, property.Expression, localRange, skipDisabledElements, visitedValues))
                yield return source;

            bool targetExpressionOverrides = localRange is not null && property.Expression is not null;
            bool targetBaseOverridden = localRange is not null && (targetExpressionOverrides || AnimationSuppliesValue(property.Animation));
            if (!targetBaseOverridden)
            {
                foreach (IFileSource source in EnumeratePropertyValueFileSources(property.CurrentValue, localRange, skipDisabledElements, visitedValues))
                    yield return source;
            }

            if (!targetExpressionOverrides)
            {
                foreach (IFileSource source in EnumerateAnimatedFileSources(property.Animation, localRange, skipDisabledElements, visitedValues))
                    yield return source;
            }

            yield break;
        }

        // Fall back to a registered CoreProperty (PropertyLookup's second strategy). CoreProperties are
        // not animatable/expressible on this surface, so only the resolved value matters.
        CoreProperty? coreProperty = PropertyRegistry.GetRegistered(target.GetType())
            .FirstOrDefault(p => string.Equals(p.Name, reference.PropertyPath, StringComparison.OrdinalIgnoreCase));
        if (coreProperty is not null && !coreProperty.PropertyType.IsValueType)
        {
            foreach (IFileSource source in EnumeratePropertyValueFileSources(target.GetValue(coreProperty), localRange, skipDisabledElements, visitedValues))
                yield return source;
        }
    }

    private static IEnumerable<IFileSource> EnumerateGraphSources(
        GraphModel model, HashSet<GraphGroup> visitedGraphGroups, TimeRange? localRange = null, HashSet<EngineObject>? visitedValues = null,
        TimeRange? sceneWindow = null)
    {
        visitedValues ??= new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);

        foreach (GraphNode node in model.Nodes)
        {
            // Every input port whose value is an IFileSource — VideoSourceNode.Source, ImageSourceNode.Source,
            // and a GroupNode's outer-boundary inputs alike. Gating on the value type (not a specific node
            // type) keeps this uniform across all source-carrying nodes.
            foreach (IFileSource source in EnumerateNodeInputSources(node, localRange, visitedValues, sceneWindow))
                yield return source;

            // A user-constructed GroupNode can reference a GraphGroup that (transitively) contains the
            // same GroupNode, producing an infinite walk. The visited set makes the recursion terminate.
            if (node is GroupNode groupNode && visitedGraphGroups.Add(groupNode.Group))
            {
                foreach (IFileSource source in EnumerateGraphSources(groupNode.Group, visitedGraphGroups, localRange, visitedValues, sceneWindow))
                    yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateNodeInputSources(
        GraphNode node, TimeRange? localRange, HashSet<EngineObject> visitedValues, TimeRange? sceneWindow)
    {
        // GraphSnapshot.LoadAnimatedValues evaluates a node's non-global input animations at
        // time - node.Start, so shift the window into node-local time before filtering; without this an
        // in-window keyframe on a time-offset node could be wrongly dropped. A global-clock input is
        // sampled at scene time, not node-local time, so it filters against the unshifted sceneWindow.
        TimeRange? nodeWindow = localRange?.SubtractStart(node.Start);

        foreach (INodeMember member in node.Items)
        {
            if (member is not IInputPort inputPort || inputPort.Property is not { } property)
                continue;

            // A connected input's value comes from the upstream node (LoadAnimatedValues skips it), so
            // this port's own base/animation is never opened — the upstream node reports its sources.
            if (inputPort.Connection.Value is not null)
                continue;

            IAnimation? animation = (property as IAnimatablePropertyAdapter)?.Animation;

            // Like the EngineObject property walk: when range-filtering, the render samples an input's
            // animation, not its base value, so an overridden stale base must not block export.
            bool baseOverridden = nodeWindow is not null && AnimationSuppliesValue(animation);
            if (!baseOverridden)
            {
                // The current value can be a direct IFileSource or an EngineObject holding nested
                // sources (GeometryShapeNode.Fill set to an ImageBrush that ToResource opens), so route
                // it through the same recursion the animated values use.
                foreach (IFileSource source in EnumeratePropertyValueFileSources(property.GetValue(), nodeWindow, skipDisabledElements: false, visitedValues))
                    yield return source;
            }

            if (animation is not null)
            {
                foreach (IFileSource source in EnumerateAnimatedFileSources(animation, nodeWindow, visitedValues: visitedValues, sceneWindow: sceneWindow))
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
        TimeRange? localRange = null,
        TimeRange? sceneWindow = null)
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
                    if (skipDisabledElements && !child.IsEnabled)
                        continue;

                    // A child effect can carry an ordinary file-source property (LutEffect.Source) the
                    // graph walker below never sees, so walk its own properties too. The Children list is
                    // not an EngineObject-valued property, so the #19 property recursion cannot reach here.
                    foreach (IFileSource source in EnumeratePropertyFileSources(child, localRange, skipDisabledElements, sceneWindow: sceneWindow))
                        yield return source;

                    foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                        child, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects, skipDisabledElements, localRange, sceneWindow))
                        yield return source;
                }

                break;

            case NodeGraphFilterEffect { Model.CurrentValue: { } model }:
                foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups, localRange, sceneWindow: sceneWindow))
                    yield return source;

                break;

            // FilterEffectPresenter and DelayAnimationEffect render a nested filter effect the
            // property walk cannot reach, so a NodeGraphFilterEffect source inside them would be
            // invisible to the Proxies tab, cache invalidation, and export preflight.
            case FilterEffectPresenter { Target.CurrentValue: { } presented }:
                // A presenter applies its target with the same context (no time remap), so the render
                // window maps directly — thread localRange, unlike the delay effect below.
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    presented, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects, skipDisabledElements, localRange, sceneWindow))
                    yield return source;

                break;

            case DelayAnimationEffect { Effect.CurrentValue: { } delayed }:
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    delayed, visitedFilterEffectGroups, visitedGraphGroups, visitedFilterEffects, skipDisabledElements))
                    yield return source;

                break;
        }
    }

    // Conservative: treat the base CurrentValue as in-play unless every keyframe is non-null.
    // KeyFrameAnimation.Interpolate returns the non-null neighbour when only one side of a pair is
    // null, so a lone null keyframe does not always make GetAnimatedValue return null — but it CAN
    // (a trailing/sole null keyframe, or a span of consecutive nulls), and pinpointing exactly when
    // would risk dropping a base the render falls back to (GetAnimatedValue(...) ?? CurrentValue).
    // Over-reporting the base is the safe direction: it never omits a file the render opens.
    private static bool AnimationSuppliesValue(IAnimation? animation)
        => animation is KeyFrameAnimation { KeyFrames.Count: > 0 } keyFrameAnimation
           && keyFrameAnimation.KeyFrames.All(k => k.Value is not null);

    private static IEnumerable<IFileSource> EnumerateAnimatedFileSources(
        IAnimation? animation, TimeRange? localRange = null, bool skipDisabledElements = false, HashSet<EngineObject>? visitedValues = null,
        TimeRange? sceneWindow = null)
    {
        if (animation is not KeyFrameAnimation keyFrameAnimation)
            yield break;

        visitedValues ??= new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);

        // Global-clock keyframes are sampled at scene time, element-local keyframes at localRange. Pick
        // the matching window; a null sceneWindow (descent crossed a time remap, so scene time is no
        // longer known) falls back to the broad walk for global-clock keyframes.
        TimeRange? window = keyFrameAnimation.UseGlobalClock ? sceneWindow : localRange;

        IReadOnlyList<IKeyFrame> keyFrames = keyFrameAnimation.KeyFrames;
        for (int i = 0; i < keyFrames.Count; i++)
        {
            // A keyframe value is either a direct IFileSource or an EngineObject holding nested sources
            // (an animated Fill switching to an ImageBrush, an animated LutEffect, …); a null value
            // contributes nothing (the render falls back to the base, handled by AnimationSuppliesValue).
            if (keyFrames[i].Value is not { } value)
                continue;

            // With no render window every keyframe value counts (proxy scan / badge enumeration). With
            // one, keep a keyframe only if a sample inside the window could resolve to it. For an object
            // value (IFileSource) KeyFrameAnimation.Interpolate returns the NEXT keyframe's value, and
            // GetPreviousAndNextKeyFrame picks a key as `next` when `prev.KeyTime < t <= this.KeyTime`, so
            // keyframe i is the sampled value on the left-open span (previous key time, this key time] —
            // the last one holds forward to +inf. A zero-duration window is a point sample {Start} (kept
            // iff the point lies in the span), NOT an empty half-open range; a non-empty window [Start,End)
            // keeps the span iff (spanStart, spanEnd] and [Start, End) overlap.
            if (window is { } range)
            {
                TimeSpan spanStart = i > 0 ? keyFrames[i - 1].KeyTime : TimeSpan.MinValue;
                TimeSpan spanEnd = i < keyFrames.Count - 1 ? keyFrames[i].KeyTime : TimeSpan.MaxValue;
                bool keep = range.Duration <= TimeSpan.Zero
                    ? spanStart < range.Start && range.Start <= spanEnd
                    : spanStart < range.End && range.Start <= spanEnd;
                if (!keep)
                    continue;
            }

            foreach (IFileSource source in EnumeratePropertyValueFileSources(value, localRange, skipDisabledElements, visitedValues))
                yield return source;
        }
    }
}
