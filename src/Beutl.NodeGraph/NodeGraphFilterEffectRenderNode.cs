using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.NodeGraph;

internal class NodeGraphFilterEffectRenderNode(NodeGraphFilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    private readonly CompositionContext _compositionContext = new(TimeSpan.Zero);

    private NodeGraphFilterEffect.Resource? GraphResource => FilterEffect?.Resource as  NodeGraphFilterEffect.Resource;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        var model = GraphResource?._model;
        var lastTime = GraphResource?._lastTime;
        if (GraphResource == null || model == null || lastTime == null)
            return context.Input;

        // 1. FilterEffectInputNode の OperationWrapperRenderNode を見つける（Build 時に作成済み）
        OperationWrapperRenderNode? inputWrapper = FindInputWrapper(model);
        if (inputWrapper == null)
            return context.Input;

        // 2. 入力 operations を OperationWrapperRenderNode に設定（Evaluate の前に行う）
        inputWrapper.SetOperations(context.Input);

        // 3. グラフのノードを評価
        _compositionContext.Time = lastTime.Value;
        GraphResource._snapshot.Evaluate(CompositionTarget.Graphics, _compositionContext);

        // 4. OutputNode から出力 RenderNode を収集
        var outputRenderNodes = PullOutputValue(model);
        if (outputRenderNodes.Count == 0)
            return context.Input;

        // 5. RenderNodeProcessor でグラフ出力ツリーを処理
        var allResults = new List<RenderNodeOperation>();
        foreach (RenderNode outputNode in outputRenderNodes)
        {
            var processor = new RenderNodeProcessor(outputNode, context.IsRenderCacheEnabled);
            allResults.AddRange(processor.PullToRoot());
        }

        inputWrapper.SetOperations([]);
        return allResults.ToArray();
    }

    private OperationWrapperRenderNode? FindInputWrapper(GraphModel model)
    {
        foreach (var node in model.Nodes)
        {
            if (node is FilterEffectInputNode)
            {
                int slotIndex = GraphResource!._snapshot.FindSlotIndex(node);
                if (slotIndex < 0) continue;
                var resource = GraphResource!._snapshot.GetResource(slotIndex);
                if (resource is FilterEffectInputNode.Resource inputResource)
                    return inputResource.Wrapper;
            }
        }

        return null;
    }

    private List<RenderNode> PullOutputValue(GraphModel model)
    {
        var result = new List<RenderNode>();
        foreach (var node in model.Nodes)
        {
            if (node is OutputNode outputNode)
            {
                int slotIndex = GraphResource!._snapshot.FindSlotIndex(outputNode);
                if (slotIndex < 0) continue;

                var resource = GraphResource!._snapshot.GetResource(slotIndex);
                if (resource == null) continue;

                if (!resource.ItemIndexMap.TryGetValue(outputNode.InputPort, out int itemIndex))
                    continue;

                IItemValue? itemValue = GraphResource!._snapshot.GetItemValue(slotIndex, itemIndex);
                if (itemValue?.GetBoxed() is RenderNode renderNode)
                {
                    result.Add(renderNode);
                }
            }
        }

        return result;
    }
}
