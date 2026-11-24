using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.Editor;

public static partial class ProxySourceEnumerator
{
    // Graph list properties also reach the structural group/decorator walk. Share its guards so
    // those two paths do not duplicate sources or recurse through a presenter back to the list.
    private static bool TryVisitGraphDrawable(Drawable drawable, TimeRange? localRange, ObjectWalkContext context)
    {
        if (localRange == null)
        {
            if (!context.VisitedFullWalkTargets.Add(drawable)) return false;
            context.VisitedTargets.Add(drawable);
            return true;
        }
        return !context.VisitedFullWalkTargets.Contains(drawable) && context.VisitedTargets.Add(drawable);
    }

    private static IEnumerable<FileSourceUse> EnumerateGraphSources(
        GraphModel model, HashSet<GraphGroup> visitedGraphGroups, TimeRange? localRange = null,
        TimeRange? sceneWindow = null, ObjectWalkContext? walkContext = null, bool skipDisabledElements = false)
    {
        var visitedValues = new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);

        // Use property identity so aliases are treated like rendering, while an unrelated property
        // holding the same file still contributes that file. Keep this scoped to the graph walk;
        // the broad project-asset walk must retain stored values for later disconnection.
        var connectedInputs = new HashSet<IProperty>(ReferenceEqualityComparer.Instance);
        if (walkContext?.ConnectedNodeInputs is { } inherited) connectedInputs.UnionWith(inherited);
        // Resolve all descendant writers before visiting any aliases in an outer graph.
        foreach (GraphNode node in model.EnumerateGraphs().SelectMany(graph => graph.Nodes))
        {
            foreach (IInputPort port in node.EnumerateMembers().OfType<IInputPort>())
            {
                if (port.Connection.Value.Value != null && node.CanConnectInput(port)
                    && port.Property?.GetEngineProperty() is { } property)
                    connectedInputs.Add(property);
            }
        }
        walkContext ??= new ObjectWalkContext([], visitedGraphGroups,
            new HashSet<Drawable>(ReferenceEqualityComparer.Instance), new HashSet<Drawable>(ReferenceEqualityComparer.Instance), sceneWindow);
        walkContext = walkContext with { ConnectedNodeInputs = connectedInputs };

        foreach (FileSourceUse source in WalkGraph(model)) yield return source;

        IEnumerable<FileSourceUse> WalkGraph(GraphModel graph)
        {
            foreach (GraphNode node in graph.Nodes)
            {
                foreach (FileSourceUse source in EnumerateNodePropertySources(node, localRange, visitedValues, sceneWindow, walkContext, skipDisabledElements))
                    yield return source;

                // Reuse the complete input set without rescanning each descendant subtree.
                if (node is GroupNode groupNode && visitedGraphGroups.Add(groupNode.Group.Value))
                {
                    foreach (FileSourceUse source in WalkGraph(groupNode.Group.Value)) yield return source;
                }
            }
        }
    }

    private static IEnumerable<FileSourceUse> EnumerateNodePropertySources(
        GraphNode node, TimeRange? localRange, HashSet<EngineObject> visitedValues, TimeRange? sceneWindow, ObjectWalkContext walkContext,
        bool skipDisabledElements = false)
    {
        // GraphSnapshot.LoadAnimatedValues evaluates a node's non-global property animations at
        // time - node.Start, so shift the window into node-local time before filtering; without this an
        // in-window keyframe on a time-offset node could be wrongly dropped. A global-clock input is
        // sampled at scene time, not node-local time, so it filters against the unshifted sceneWindow.
        TimeRange? nodeWindow = localRange?.SubtractStart(node.Start);

        foreach (INodeMember member in node.Items)
        {
            // Layer inputs store their authored values on outputs, which LoadAnimatedValues reads
            // just like input properties. Computed outputs do not carry these stored values.
            if (member is not (IInputPort or LayerInputNode.ILayerInputPort) || member.Property is not { } property)
                continue;

            // Accepted connections supply the input from upstream. Rejected retained connections
            // use local values again, just as GraphSnapshot does.
            if (member is IInputPort inputPort && inputPort.Connection.Value.Value is not null && node.CanConnectInput(inputPort)
                || property.GetEngineProperty() is { } engineProperty
                && walkContext.ConnectedNodeInputs?.Contains(engineProperty) == true)
                continue;

            var settings = (member is LayerInputNode.ILayerInputPort output
                    ? EnumerateConnectedVideoSettings(output)
                    : EnumerateInputVideoSettings(node, (IInputPort)member, visited: null))
                .Distinct()
                .ToArray();
            if (settings.Length == 0) continue;

            IKeyFrameAnimation? animation = (property as IAnimatablePropertyAdapter)?.Animation;

            // Like the EngineObject property walk: when range-filtering, the render samples an input's
            // animation, not its base value, so an overridden stale base must not block export.
            bool baseOverridden = nodeWindow is not null && AnimationSuppliesValue(animation);
            if (!baseOverridden)
            {
                // The current value can be a direct IFileSource or an EngineObject holding nested
                // sources (GeometryShapeNode.Fill set to an ImageBrush that ToResource opens), so route
                // it through the same recursion the animated values use.
                foreach (FileSourceUse source in EnumeratePropertyValueFileSources(property.GetValue(), nodeWindow, skipDisabledElements, visitedValues, walkContext))
                    foreach (var use in WithVideoSettings(source, settings))
                        yield return use;
            }

            if (animation is not null)
            {
                foreach (FileSourceUse source in EnumerateAnimatedFileSources(animation, nodeWindow, skipDisabledElements, visitedValues: visitedValues, sceneWindow: sceneWindow, walkContext: walkContext))
                    foreach (var use in WithVideoSettings(source, settings))
                        yield return use;
            }
        }
    }

    private static IEnumerable<VideoDeinterlaceOptions?> EnumerateConnectedVideoSettings(
        IOutputPort output, HashSet<IOutputPort>? visited = null)
    {
        visited ??= new(ReferenceEqualityComparer.Instance);
        if (!visited.Add(output)) yield break;

        foreach (var reference in output.Connections)
        {
            if (reference.Value?.Input.Value.Value is not IInputPort input
                || input.FindHierarchicalParent<GraphNode>() is not { } node || !node.CanConnectInput(input))
                continue;

            foreach (var setting in EnumerateInputVideoSettings(node, input, visited))
                yield return setting;
        }
    }

    private static IEnumerable<VideoDeinterlaceOptions?> EnumerateInputVideoSettings(
        GraphNode node, IInputPort input, HashSet<IOutputPort>? visited)
    {
        IOutputPort? output = node switch
        {
            GroupNode group => group.FindInnerInput(input),
            GroupOutput boundary => boundary.FindHierarchicalParent<GroupNode>()?.FindOuterOutput(input),
            _ => null,
        };
        return output != null
            ? EnumerateConnectedVideoSettings(output, visited)
            : [GetVideoSettings(input.Property?.GetEngineProperty())];
    }

    private static IEnumerable<FileSourceUse> WithVideoSettings(FileSourceUse use, VideoDeinterlaceOptions?[] settings)
    {
        if (use.Source is not VideoSource)
        {
            yield return use;
            yield break;
        }

        foreach (var deinterlace in settings)
            yield return use with { Deinterlace = deinterlace ?? use.Deinterlace };
    }

    private static IEnumerable<FileSourceUse> EnumerateFilterEffectGraphSources(
        FilterEffect? effect,
        HashSet<GraphGroup> visitedGraphGroups,
        HashSet<FilterEffect> visitedFilterEffects,
        HashSet<FilterEffect> visitedFullWalkFilterEffects,
        bool skipDisabledElements,
        TimeRange? localRange = null,
        TimeRange? sceneWindow = null,
        ObjectWalkContext? walkContext = null)
    {
        if (effect is null)
            yield break;

        // Presenter/delay targets are reference properties, so a filter chain is user-cyclable;
        // the visited sets make the recursion terminate. A full walk (localRange is null: a remapped
        // DelayAnimationEffect target, or the top-level chain) enumerates every source; a windowed walk
        // only those inside localRange. So a completed full walk supersedes any later walk of the same
        // effect, but a windowed walk must NOT suppress a later full walk that would surface
        // out-of-window sources — hence the separate full-walk visited set.
        if (localRange is null)
        {
            if (!visitedFullWalkFilterEffects.Add(effect))
                yield break;

            visitedFilterEffects.Add(effect);
        }
        else if (visitedFullWalkFilterEffects.Contains(effect) || !visitedFilterEffects.Add(effect))
        {
            yield break;
        }

        // FilterEffectRenderNode returns its input unchanged for a disabled effect, so a source inside
        // a disabled filter (or filter group) never renders; export preflight must not demand its file.
        if (skipDisabledElements && !effect.IsEnabled.Value)
            yield break;

        switch (effect)
        {
            case FilterEffectGroup group:
                foreach (FilterEffect child in group.Children)
                {
                    if (skipDisabledElements && !child.IsEnabled.Value)
                        continue;

                    // A child effect can carry an ordinary file-source property (LutEffect.Source) the
                    // graph walker below never sees, so walk its own properties too. Children is not an
                    // EngineObject-valued property, so the property recursion does not reach it.
                    foreach (FileSourceUse source in EnumeratePropertyFileSources(child, localRange, skipDisabledElements, sceneWindow: sceneWindow, walkContext: walkContext))
                        yield return source;

                    foreach (FileSourceUse source in EnumerateFilterEffectGraphSources(
                        child, visitedGraphGroups, visitedFilterEffects, visitedFullWalkFilterEffects, skipDisabledElements, localRange, sceneWindow, walkContext))
                        yield return source;
                }

                break;

            case NodeGraphFilterEffect { Model.Value: { } model }:
                foreach (FileSourceUse source in EnumerateGraphSources(model, visitedGraphGroups, localRange, sceneWindow: sceneWindow, walkContext: walkContext, skipDisabledElements: skipDisabledElements))
                    yield return source;

                break;

            // FilterEffectPresenter and DelayAnimationEffect render a nested filter effect the
            // property walk cannot reach, so a NodeGraphFilterEffect source inside them would be
            // invisible to the Proxies tab, cache invalidation, and export preflight.
            case FilterEffectPresenter presenter
                when ResolveExpressionValue<FilterEffect>(presenter, presenter.Target, walkContext: walkContext) is { } presented:
                // A presenter applies its target with the same context (no time remap), so the render
                // window maps directly — thread localRange, unlike the delay effect below. The resource
                // renders the effective Target, so resolve an expression-supplied one.
                foreach (FileSourceUse source in EnumerateFilterEffectGraphSources(
                    presented, visitedGraphGroups, visitedFilterEffects, visitedFullWalkFilterEffects, skipDisabledElements, localRange, sceneWindow, walkContext))
                    yield return source;

                break;

            case DelayAnimationEffect delay
                when ResolveExpressionValue<FilterEffect>(delay, delay.Effect, walkContext: walkContext) is { } delayed:
                foreach (FileSourceUse source in EnumerateFilterEffectGraphSources(
                    delayed, visitedGraphGroups, visitedFilterEffects, visitedFullWalkFilterEffects, skipDisabledElements, walkContext: walkContext))
                    yield return source;

                break;
        }
    }
}
