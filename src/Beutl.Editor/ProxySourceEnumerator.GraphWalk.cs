using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.IO;
using Beutl.Media;
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

    private static IEnumerable<IFileSource> EnumerateGraphSources(
        GraphModel model, HashSet<GraphGroup> visitedGraphGroups, TimeRange? localRange = null, HashSet<EngineObject>? visitedValues = null,
        TimeRange? sceneWindow = null, ObjectWalkContext? walkContext = null, bool skipDisabledElements = false)
    {
        visitedValues ??= new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);

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
                if (port.Connection.Value != null && node.CanConnectInput(port)
                    && port.Property?.GetEngineProperty() is { } property)
                    connectedInputs.Add(property);
            }
        }
        walkContext ??= new ObjectWalkContext([], visitedGraphGroups,
            new HashSet<Drawable>(ReferenceEqualityComparer.Instance), new HashSet<Drawable>(ReferenceEqualityComparer.Instance), null, sceneWindow);
        walkContext = walkContext with { ConnectedNodeInputs = connectedInputs };

        foreach (IFileSource source in WalkGraph(model)) yield return source;

        IEnumerable<IFileSource> WalkGraph(GraphModel graph)
        {
            foreach (GraphNode node in graph.Nodes)
            {
                // Keep every group's outer inputs even when its inner graph is shared.
                foreach (IFileSource source in EnumerateNodePropertySources(node, localRange, visitedValues, sceneWindow, walkContext, skipDisabledElements))
                    yield return source;

                // Reuse the complete input set without rescanning each descendant subtree.
                if (node is GroupNode groupNode && visitedGraphGroups.Add(groupNode.Group))
                {
                    foreach (IFileSource source in WalkGraph(groupNode.Group)) yield return source;
                }
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateNodePropertySources(
        GraphNode node, TimeRange? localRange, HashSet<EngineObject> visitedValues, TimeRange? sceneWindow, ObjectWalkContext? walkContext = null,
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

            if (member is LayerInputNode.ILayerInputPort output
                && !output.Connections.Any(reference => reference.Value?.Input.Value is IInputPort consumer
                    && consumer.FindHierarchicalParent<GraphNode>()?.CanConnectInput(consumer) == true))
                continue;

            // Accepted connections supply the input from upstream. Rejected retained connections
            // use local values again, just as GraphSnapshot does.
            if (member is IInputPort inputPort && inputPort.Connection.Value is not null && node.CanConnectInput(inputPort)
                || property.GetEngineProperty() is { } engineProperty
                && walkContext?.ConnectedNodeInputs?.Contains(engineProperty) == true)
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
                foreach (IFileSource source in EnumeratePropertyValueFileSources(property.GetValue(), nodeWindow, skipDisabledElements, visitedValues, walkContext))
                    yield return source;
            }

            if (animation is not null)
            {
                foreach (IFileSource source in EnumerateAnimatedFileSources(animation, nodeWindow, skipDisabledElements, visitedValues: visitedValues, sceneWindow: sceneWindow, walkContext: walkContext))
                    yield return source;
            }
        }
    }

    private static IEnumerable<IFileSource> EnumerateFilterEffectGraphSources(
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
        if (skipDisabledElements && !effect.IsEnabled)
            yield break;

        switch (effect)
        {
            case FilterEffectGroup group:
                foreach (FilterEffect child in group.Children)
                {
                    if (skipDisabledElements && !child.IsEnabled)
                        continue;

                    // A child effect can carry an ordinary file-source property (LutEffect.Source) the
                    // graph walker below never sees, so walk its own properties too. The Children list is
                    // not an EngineObject-valued property, so the #19 property recursion cannot reach here.
                    foreach (IFileSource source in EnumeratePropertyFileSources(child, localRange, skipDisabledElements, sceneWindow: sceneWindow, walkContext: walkContext))
                        yield return source;

                    foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                        child, visitedGraphGroups, visitedFilterEffects, visitedFullWalkFilterEffects, skipDisabledElements, localRange, sceneWindow, walkContext))
                        yield return source;
                }

                break;

            case NodeGraphFilterEffect { Model.CurrentValue: { } model }:
                foreach (IFileSource source in EnumerateGraphSources(model, visitedGraphGroups, localRange, sceneWindow: sceneWindow, walkContext: walkContext, skipDisabledElements: skipDisabledElements))
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
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    presented, visitedGraphGroups, visitedFilterEffects, visitedFullWalkFilterEffects, skipDisabledElements, localRange, sceneWindow, walkContext))
                    yield return source;

                break;

            case DelayAnimationEffect delay
                when ResolveExpressionValue<FilterEffect>(delay, delay.Effect, walkContext: walkContext) is { } delayed:
                foreach (IFileSource source in EnumerateFilterEffectGraphSources(
                    delayed, visitedGraphGroups, visitedFilterEffects, visitedFullWalkFilterEffects, skipDisabledElements, walkContext: walkContext))
                    yield return source;

                break;
        }
    }
}
