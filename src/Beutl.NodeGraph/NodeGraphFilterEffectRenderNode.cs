using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.NodeGraph;

internal class NodeGraphFilterEffectRenderNode(NodeGraphFilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    private static readonly IEqualityComparer<RenderNode> s_renderNodeReferenceComparer =
        ReferenceEqualityComparer.Instance;
    private readonly CompositionContext _compositionContext = new(TimeSpan.Zero);

    private NodeGraphFilterEffect.Resource? GraphResource => FilterEffect?.Resource as NodeGraphFilterEffect.Resource;

    public override void Process(RenderNodeContext context)
    {
        NodeGraphFilterEffect.Resource? graphResource = GraphResource;
        var model = graphResource?.Model;
        var lastTime = graphResource?.LastTime;
        if (graphResource == null || !graphResource.IsEnabled || model == null || lastTime == null)
        {
            context.PassThrough();
            return;
        }

        FilterEffectInputRenderNode? inputFacade = FindInputFacade(model, graphResource);
        if (inputFacade == null)
        {
            context.PassThrough();
            return;
        }

        using (FilterEffectInputBinding binding = inputFacade.Bind(context))
        {
            _compositionContext.Time = lastTime.Value;
            _compositionContext.PreferProxy = graphResource.PreferProxy;
            _compositionContext.PreferredProxyPreset = graphResource.PreferredProxyPreset;
            _compositionContext.DisableResourceShare = graphResource.DisableResourceShare;
            graphResource.Snapshot.Evaluate(CompositionTarget.Graphics, _compositionContext);

            var outputRenderNodes = PullOutputValue(model, graphResource);
            if (outputRenderNodes.Count == 0)
            {
                context.PassThrough();
            }
            else
            {
                foreach (IGrouping<RenderNode, RenderNode> repeated in outputRenderNodes
                             .GroupBy(static node => node, s_renderNodeReferenceComparer)
                             .Where(static group => group.Skip(1).Any()))
                {
                    binding.EnsureFanOutSafe(repeated.Key);
                }

                foreach (RenderNode outputNode in outputRenderNodes)
                {
                    context.PublishRange(binding.RecordSubtreeForPublication(outputNode));
                }
            }

            binding.PublishDeferredPreviews();
        }
    }

    private static FilterEffectInputRenderNode? FindInputFacade(
        GraphModel model,
        NodeGraphFilterEffect.Resource graphResource)
    {
        foreach (var node in model.Nodes)
        {
            if (node is FilterEffectInputNode)
            {
                int slotIndex = graphResource.Snapshot.FindSlotIndex(node);
                if (slotIndex < 0) continue;
                var resource = graphResource.Snapshot.GetResource(slotIndex);
                if (resource is FilterEffectInputNode.Resource inputResource)
                    return inputResource.InputFacade;
            }
        }

        return null;
    }

    private static List<RenderNode> PullOutputValue(
        GraphModel model,
        NodeGraphFilterEffect.Resource graphResource)
    {
        var result = new List<RenderNode>();
        foreach (var node in model.Nodes)
        {
            if (node is OutputNode outputNode)
            {
                int slotIndex = graphResource.Snapshot.FindSlotIndex(outputNode);
                if (slotIndex < 0) continue;

                var resource = graphResource.Snapshot.GetResource(slotIndex);
                if (resource == null) continue;

                if (!resource.ItemIndexMap.TryGetValue(outputNode.InputPort, out int itemIndex))
                    continue;

                IItemValue? itemValue = graphResource.Snapshot.GetItemValue(slotIndex, itemIndex);
                if (itemValue?.GetBoxed() is RenderNode renderNode)
                {
                    result.Add(renderNode);
                }
            }
        }

        return result;
    }
}

internal sealed class FilterEffectInputBinding : IDisposable
{
    private static readonly AsyncLocal<FilterEffectInputBinding?> s_current = new();
    private static readonly object s_previewCommandStructuralKey = new();
    private readonly RenderNodeContext _context;
    private readonly FilterEffectInputRenderNode _inputFacade;
    private readonly IReadOnlyList<RenderFragmentHandle> _graphInputs;
    private readonly FilterEffectInputBinding? _previous;
    private readonly Dictionary<RenderNode, IReadOnlyList<RenderFragmentHandle>> _recordedSubtrees =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<RenderNode> _activeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<RenderNode> _consumedNonFanOutSubtrees = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeferredPreview> _previews = [];
    private bool _disposed;

    internal FilterEffectInputBinding(
        FilterEffectInputRenderNode inputFacade,
        RenderNodeContext context)
    {
        _inputFacade = inputFacade;
        _context = context;
        _graphInputs = context.Inputs;
        _previous = s_current.Value;
        s_current.Value = this;
    }

    internal static bool TryGetCurrent(out FilterEffectInputBinding binding)
    {
        binding = s_current.Value!;
        return binding is not null && !binding._disposed;
    }

    internal IReadOnlyList<RenderFragmentHandle> RecordSubtree(RenderNode node)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(node);
        if (_recordedSubtrees.TryGetValue(node, out IReadOnlyList<RenderFragmentHandle>? cached))
            return cached;

        if (!_activeNodes.Add(node))
        {
            throw new InvalidOperationException(
                $"A node-graph render cycle was detected at '{node.GetType().FullName}'.");
        }

        try
        {
            IReadOnlyList<RenderFragmentHandle> result;
            if (ReferenceEquals(node, _inputFacade))
            {
                result = _context.RecordNode(node, _graphInputs);
            }
            else if (node is ContainerRenderNode container)
            {
                var inputs = new List<RenderFragmentHandle>();
                foreach (RenderNode child in container.Children)
                {
                    IReadOnlyList<RenderFragmentHandle> childOutputs = RecordSubtree(child);
                    MarkSubtreeConsumed(child, childOutputs);
                    inputs.AddRange(childOutputs);
                }

                result = _context.RecordNode(node, inputs);
            }
            else
            {
                result = _context.RecordNode(node, []);
            }

            _recordedSubtrees.Add(node, result);
            return result;
        }
        finally
        {
            _activeNodes.Remove(node);
        }
    }

    internal IReadOnlyList<RenderFragmentHandle> RecordSubtreeForPublication(RenderNode node)
    {
        IReadOnlyList<RenderFragmentHandle> outputs = RecordSubtree(node);
        MarkSubtreeConsumed(node, outputs);
        return outputs;
    }

    internal bool TryMeasureSubtree(RenderNode node, out Rect bounds)
    {
        IReadOnlyList<RenderFragmentHandle> outputs = RecordSubtree(node);
        return TryCalculateBounds(outputs, out bounds);
    }

    internal void EnsureFanOutSafe(RenderNode node)
    {
        IReadOnlyList<RenderFragmentHandle> outputs = RecordSubtree(node);
        if (outputs.All(static output => output.CanBeUsedAsValueInput))
            return;

        ReplaceWithFiniteLayer(node, outputs);
    }

    internal void RegisterPreview(
        RenderNode? node,
        Func<Ref<Bitmap>?, Ref<Bitmap>?> replace,
        object runtimeIdentity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(replace);
        ArgumentNullException.ThrowIfNull(runtimeIdentity);

        if (node is null)
        {
            _previews.Add(new DeferredPreview([], replace, runtimeIdentity));
            return;
        }

        IReadOnlyList<RenderFragmentHandle> outputs = RecordSubtree(node);
        if (outputs.Count == 0
            || !TryCalculateBounds(outputs, out Rect bounds)
            || bounds.Width == 0
            || bounds.Height == 0)
        {
            _previews.Add(new DeferredPreview([], replace, runtimeIdentity));
            return;
        }

        if (outputs is [RenderFragmentHandle single]
            && single.CanBeUsedAsValueInput
            && single.ValueCardinality.Maximum != 0)
        {
            _previews.Add(new DeferredPreview([single], replace, runtimeIdentity));
            return;
        }

        // A finite layer preserves painter order and normalizes multiple or mixed subtree outputs
        // to one readback-eligible value for the deferred preview command. When a raw output cannot
        // fan out, replace the identity cache as well so later graph outputs share the layer instead.
        RenderFragmentHandle layer = _context.Layer(outputs, bounds);
        if (outputs.Any(static output => !output.CanBeUsedAsValueInput))
        {
            MarkSubtreeConsumed(node, outputs);
            _recordedSubtrees[node] = [layer];
        }
        _previews.Add(new DeferredPreview([layer], replace, runtimeIdentity));
    }

    internal void PublishDeferredPreviews()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (DeferredPreview preview in _previews)
        {
            Func<Ref<Bitmap>?, Ref<Bitmap>?> replace = preview.Replace;
            IReadOnlyList<RenderFragmentHandle> inputs = preview.Inputs;
            bool requiresInputReadback = inputs.Count != 0;
            object runtimeIdentity = preview.RuntimeIdentity;
            TargetCommandDescription description = TargetCommandDescription.Create(
                session => ExecutePreview(session, replace),
                TargetRegion.Empty,
                Rect.Empty,
                RenderHitTestContract.None,
                TargetAccess.ReadWrite,
                requiresInputReadback,
                structuralKey: s_previewCommandStructuralKey,
                runtimeIdentity: new RenderRuntimeIdentity(runtimeIdentity));
            _context.Publish(_context.TargetCommand(inputs, description));
        }

        _previews.Clear();
    }

    private IReadOnlyList<RenderFragmentHandle> ReplaceWithFiniteLayer(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> outputs)
    {
        if (outputs.Count == 0
            || !TryCalculateBounds(outputs, out Rect bounds)
            || bounds.Width == 0
            || bounds.Height == 0)
        {
            throw new InvalidOperationException(
                $"The shared node-graph subtree '{node.GetType().FullName}' cannot be normalized "
                + "because it has no finite non-empty recording bounds.");
        }

        MarkSubtreeConsumed(node, outputs);
        IReadOnlyList<RenderFragmentHandle> normalized = [_context.Layer(outputs, bounds)];
        _recordedSubtrees[node] = normalized;
        return normalized;
    }

    private void MarkSubtreeConsumed(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> outputs)
    {
        if (outputs.All(static output => output.CanBeUsedAsValueInput))
            return;

        // A non-value fragment cannot fan out. If its identity reappears after one parent has already
        // consumed it, normalization is no longer safe because the first parent transaction is recorded.
        // Fail here with the NodeGraph identity rather than later in transaction fan-out validation.
        if (!_consumedNonFanOutSubtrees.Add(node))
        {
            throw new InvalidOperationException(
                $"The non-value node-graph subtree '{node.GetType().FullName}' is used by more than one consumer. "
                + "Wrap the shared subtree in a finite value-producing layer before branching.");
        }
    }

    private static bool TryCalculateBounds(
        IReadOnlyList<RenderFragmentHandle> fragments,
        out Rect bounds)
    {
        Rect result = Rect.Empty;
        foreach (RenderFragmentHandle fragment in fragments)
        {
            if (!fragment.TryGetMetadata(out RenderFragmentMetadata metadata))
            {
                bounds = Rect.Empty;
                return false;
            }

            result = result.Union(metadata.Bounds);
        }

        bounds = result;
        return true;
    }

    private static void ExecutePreview(
        TargetCommandSession session,
        Func<Ref<Bitmap>?, Ref<Bitmap>?> replace)
    {
        Ref<Bitmap>? replacement = null;
        Ref<Bitmap>? previous = null;

        try
        {
            if (session.Inputs.Count == 1)
            {
                session.Inputs[0].UseSnapshot(
                    bitmap => replacement = Ref<Bitmap>.Create(bitmap.Clone()));
            }

            previous = replace(replacement);
            replacement = null;
        }
        finally
        {
            replacement?.Dispose();
            previous?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _previews.Clear();
        _recordedSubtrees.Clear();
        _activeNodes.Clear();
        _consumedNonFanOutSubtrees.Clear();
        if (ReferenceEquals(s_current.Value, this))
            s_current.Value = _previous;
    }

    private sealed record DeferredPreview(
        IReadOnlyList<RenderFragmentHandle> Inputs,
        Func<Ref<Bitmap>?, Ref<Bitmap>?> Replace,
        object RuntimeIdentity);
}
