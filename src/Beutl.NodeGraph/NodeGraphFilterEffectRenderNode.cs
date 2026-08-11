using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.NodeGraph;

// RenderNode.ChildNodes is deliberately left empty here. The graph output nodes this records through are read
// back out of the snapshot by PullOutputValue and only exist once Evaluate has run for this frame's time and
// composition flags; the next Snapshot.Build disposes them, so an array retained to back a span would hand the
// traversals disposed nodes. Revalidation and cache recursion therefore stop at this node: the graph subtree
// keeps its marks and is never render-cached. That is sound only while this node itself stays out of the cache,
// which NodeGraphFilterEffect.Resource.Update guarantees by bumping Version on every build.
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
            _compositionContext.TargetDomain = context.TargetDomain;
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
    private static readonly RenderResourceSlot<Func<Ref<Bitmap>?, Ref<Bitmap>?>> s_previewSinkSlot = new();
    private static readonly TargetCommandDefinition<PreviewCommandState> s_emptyPreviewCommand =
        CreatePreviewCommand([]);
    private static readonly TargetCommandDefinition<PreviewCommandState> s_singlePreviewCommand =
        CreatePreviewCommand([RenderInputReadback.Values([0])]);
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
        RenderNode? canonicalNode = GetCanonicalNode(node);
        if (canonicalNode == null)
            return [];

        if (_recordedSubtrees.TryGetValue(canonicalNode, out IReadOnlyList<RenderFragmentHandle>? cached))
            return cached;

        if (!_activeNodes.Add(canonicalNode))
        {
            throw new InvalidOperationException(
                $"A node-graph render cycle was detected at '{canonicalNode.GetType().FullName}'.");
        }

        try
        {
            IReadOnlyList<RenderFragmentHandle> result;
            if (ReferenceEquals(canonicalNode, _inputFacade))
            {
                result = _context.RecordNode(canonicalNode, _graphInputs);
            }
            else if (canonicalNode is ContainerRenderNode container)
            {
                var inputs = new List<RenderFragmentHandle>();
                foreach (RenderNode child in container.Children)
                {
                    IReadOnlyList<RenderFragmentHandle> childOutputs = RecordSubtree(child);
                    MarkSubtreeConsumed(child, childOutputs);
                    inputs.AddRange(childOutputs);
                }

                result = _context.RecordNode(canonicalNode, inputs);
            }
            else
            {
                result = _context.RecordNode(canonicalNode, []);
            }

            _recordedSubtrees.Add(canonicalNode, result);
            return result;
        }
        finally
        {
            _activeNodes.Remove(canonicalNode);
        }
    }

    internal IReadOnlyList<RenderFragmentHandle> RecordSubtreeForPublication(RenderNode node)
    {
        IReadOnlyList<RenderFragmentHandle> outputs = RecordSubtree(node);
        MarkSubtreeConsumed(node, outputs);
        return outputs;
    }

    internal Rect MeasureSubtree(RenderNode node)
    {
        IReadOnlyList<RenderFragmentHandle> outputs = RecordSubtree(node);
        return CalculateRecordedQueryBounds(outputs);
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
        Func<Ref<Bitmap>?, Ref<Bitmap>?> replace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(replace);
        if (node is null)
        {
            _previews.Add(new DeferredPreview([], replace));
            return;
        }

        IReadOnlyList<RenderFragmentHandle> outputs = RecordSubtree(node);
        if (outputs.Count == 0 || HasEmptyRecordedBounds(outputs))
        {
            _previews.Add(new DeferredPreview([], replace));
            return;
        }

        if (outputs is [RenderFragmentHandle single]
            && single.CanBeUsedAsValueInput
            && single.ValueCardinality.Maximum != 0)
        {
            _previews.Add(new DeferredPreview([single], replace));
            return;
        }

        // A layer preserves painter order and normalizes multiple or mixed subtree outputs
        // to one readback-eligible value for the deferred preview command. When a raw output cannot
        // fan out, replace the identity cache as well so later graph outputs share the layer instead.
        RenderFragmentHandle layer = NormalizeToLayer(outputs);
        if (outputs.Any(static output => !output.CanBeUsedAsValueInput))
        {
            MarkSubtreeConsumed(node, outputs);
            _recordedSubtrees[GetCanonicalNode(node)!] = [layer];
        }
        _previews.Add(new DeferredPreview([layer], replace));
    }

    internal void PublishDeferredPreviews()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (DeferredPreview preview in _previews)
        {
            Func<Ref<Bitmap>?, Ref<Bitmap>?> replace = preview.Replace;
            IReadOnlyList<RenderFragmentHandle> inputs = preview.Inputs;
            RenderResource<Func<Ref<Bitmap>?, Ref<Bitmap>?>> sink = _context.Borrow(replace);
            TargetCommandDefinition<PreviewCommandState> command = inputs.Count switch
            {
                0 => s_emptyPreviewCommand,
                1 => s_singlePreviewCommand,
                _ => throw new InvalidOperationException(
                    "A normalized node-graph preview must have zero or one value input."),
            };
            _context.Publish(_context.TargetCommand(
                inputs,
                command.Call(default, [s_previewSinkSlot.Bind(sink)])));
        }

        _previews.Clear();
    }

    private IReadOnlyList<RenderFragmentHandle> ReplaceWithFiniteLayer(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> outputs)
    {
        if (outputs.Count == 0 || HasEmptyRecordedBounds(outputs))
        {
            throw new InvalidOperationException(
                $"The shared node-graph subtree '{node.GetType().FullName}' cannot be normalized "
                + "because it has no finite non-empty recording bounds.");
        }

        MarkSubtreeConsumed(node, outputs);
        IReadOnlyList<RenderFragmentHandle> normalized = [NormalizeToLayer(outputs)];
        _recordedSubtrees[GetCanonicalNode(node)!] = normalized;
        return normalized;
    }

    private void MarkSubtreeConsumed(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> outputs)
    {
        if (outputs.All(static output => output.CanBeUsedAsValueInput))
            return;

        RenderNode? canonicalNode = GetCanonicalNode(node);
        if (canonicalNode == null)
            return;

        // A non-value fragment cannot fan out. If its identity reappears after one parent has already
        // consumed it, normalization is no longer safe because the first parent transaction is recorded.
        // Fail here with the NodeGraph identity rather than later in transaction fan-out validation.
        if (!_consumedNonFanOutSubtrees.Add(canonicalNode))
        {
            throw new InvalidOperationException(
                $"The non-value node-graph subtree '{canonicalNode.GetType().FullName}' is used by more than one consumer. "
                + "Wrap the shared subtree in a finite value-producing layer before branching.");
        }
    }

    private static RenderNode? GetCanonicalNode(RenderNode node)
    {
        RenderNode current = node;
        RenderNode? slow = node;
        RenderNode? fast = node;
        while (current is ReferencesChildRenderNode { Child: { IsDisposed: false } child })
        {
            current = child;
            slow = GetReferenceChild(slow);
            fast = GetReferenceChild(GetReferenceChild(fast));
            if (slow != null && ReferenceEquals(slow, fast))
            {
                throw new InvalidOperationException(
                    $"A node-graph render cycle was detected at '{slow.GetType().FullName}'.");
            }
        }

        return current is ReferencesChildRenderNode ? null : current;
    }

    private static RenderNode? GetReferenceChild(RenderNode? node)
        => node is ReferencesChildRenderNode { Child: { IsDisposed: false } child } ? child : null;

    /// <summary>
    /// Normalizes <paramref name="outputs"/> into one value-eligible layer.
    /// </summary>
    /// <remarks>
    /// The recording node observes its own local coordinate space, which every enclosing target scope
    /// separates from the request root. A symbolic subtree therefore defers its domain to graph-wide
    /// owning-target lowering, which back-maps the root domain through those scopes.
    /// </remarks>
    private RenderFragmentHandle NormalizeToLayer(IReadOnlyList<RenderFragmentHandle> outputs)
        => TryCalculateBounds(outputs, out Rect bounds)
            ? _context.Layer(outputs, bounds)
            : _context.OwningTargetLayer(outputs);

    private static bool HasEmptyRecordedBounds(IReadOnlyList<RenderFragmentHandle> outputs)
        => TryCalculateBounds(outputs, out Rect bounds)
           && (bounds.Width == 0 || bounds.Height == 0);

    private Rect CalculateRecordedQueryBounds(IReadOnlyList<RenderFragmentHandle> fragments)
    {
        Rect result = Rect.Empty;
        foreach (RenderFragmentHandle fragment in fragments)
        {
            result = result.Union(_context.GetRecordedMetadataHint(fragment).Bounds);
        }

        return result;
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

    private static TargetCommandDefinition<PreviewCommandState> CreatePreviewCommand(
        IReadOnlyList<RenderInputReadback> inputReadbacks)
        => TargetCommandDefinition<PreviewCommandState>.Create(
            static (session, _) => session.UseResource(
                s_previewSinkSlot,
                sink => ExecutePreview(session, sink)),
            TargetRegion.Empty,
            Rect.Empty,
            RenderHitTestContract.None,
            inputReadbacks: inputReadbacks,
            resources: [s_previewSinkSlot]);

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
        Func<Ref<Bitmap>?, Ref<Bitmap>?> Replace);

    private readonly record struct PreviewCommandState;
}
