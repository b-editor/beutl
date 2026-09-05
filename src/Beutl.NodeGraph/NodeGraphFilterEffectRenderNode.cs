using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.NodeGraph;

// Snapshot output nodes are disposed by the next build, so ChildNodes must not retain them.
// Revalidation and caching stop here; Resource.Update keeps this node uncached by bumping Version each build.
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
