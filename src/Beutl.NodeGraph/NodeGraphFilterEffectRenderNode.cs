using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.NodeGraph;

internal class NodeGraphFilterEffectRenderNode(NodeGraphFilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    private readonly GraphCompositionContext _compositionContext = new(TimeSpan.Zero);

    private NodeGraphFilterEffect.Resource? GraphResource => FilterEffect?.Resource as NodeGraphFilterEffect.Resource;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        var model = GraphResource?.Model;
        var lastTime = GraphResource?.LastTime;
        if (GraphResource == null || model == null || lastTime == null)
            return context.Input;

        // 1. FilterEffectInputNode の OperationWrapperRenderNode を見つける（Build 時に作成済み）
        OperationWrapperRenderNode? inputWrapper = FindInputWrapper(model);
        if (inputWrapper == null)
            return context.Input;

        // 2. 入力 operations を OperationWrapperRenderNode に設定（Evaluate の前に行う）
        inputWrapper.SetOperations(context.Input);
        var allResults = new List<RenderNodeOperation>();
        RenderNodeOperation[]? result = null;
        Exception? primaryFailure = null;
        try
        {
            // 3. グラフのノードを評価
            _compositionContext.Time = lastTime.Value;
            _compositionContext.PreferProxy = GraphResource.PreferProxy;
            _compositionContext.PreferredProxyPreset = GraphResource.PreferredProxyPreset;
            _compositionContext.DisableResourceShare = GraphResource.DisableResourceShare;
            _compositionContext.RenderIntent = context.RenderIntent;
            GraphResource.Snapshot.Evaluate(CompositionTarget.Graphics, _compositionContext);

            // 4. OutputNode から出力 RenderNode を収集
            var outputRenderNodes = PullOutputValue(model);
            if (outputRenderNodes.Count == 0)
            {
                // SetOperations transferred the input into the wrapper's ref-counted ownership. Return proxies
                // before the finally block clears the wrapper: the proxies keep those refs alive until the caller
                // disposes the passthrough output, while the wrapper itself retains nothing after Process returns.
                result = context.CreateChildProcessor(inputWrapper, context.IsRenderCacheEnabled).PullToRoot();
            }
            else
            {
                // 5. RenderNodeProcessor でグラフ出力ツリーを処理
                foreach (RenderNode outputNode in outputRenderNodes)
                {
                    // Thread the complete parent execution policy through the hosted graph.
                    var processor = context.CreateChildProcessor(outputNode, context.IsRenderCacheEnabled);
                    allResults.AddRange(processor.PullToRoot());
                }

                result = allResults.ToArray();
            }
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(allResults));
        }

        Exception? inputCleanupFailure = null;
        try
        {
            inputWrapper.SetOperations([]);
        }
        catch (Exception ex)
        {
            inputCleanupFailure = ex;
        }

        if (primaryFailure != null)
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();

        if (inputCleanupFailure != null)
        {
            // A successful graph pull has not transferred ownership to the caller yet. Discard every generated
            // output before surfacing the input teardown failure, while keeping that failure as the primary one.
            RenderNodeOperation.DisposeAll(result);
            ExceptionDispatchInfo.Capture(inputCleanupFailure).Throw();
        }

        return result ?? [];
    }

    private OperationWrapperRenderNode? FindInputWrapper(GraphModel model)
    {
        foreach (var node in model.Nodes)
        {
            if (node is FilterEffectInputNode)
            {
                int slotIndex = GraphResource!.Snapshot.FindSlotIndex(node);
                if (slotIndex < 0) continue;
                var resource = GraphResource!.Snapshot.GetResource(slotIndex);
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
                int slotIndex = GraphResource!.Snapshot.FindSlotIndex(outputNode);
                if (slotIndex < 0) continue;

                var resource = GraphResource!.Snapshot.GetResource(slotIndex);
                if (resource == null) continue;

                if (!resource.ItemIndexMap.TryGetValue(outputNode.InputPort, out int itemIndex))
                    continue;

                IItemValue? itemValue = GraphResource!.Snapshot.GetItemValue(slotIndex, itemIndex);
                if (itemValue?.GetBoxed() is RenderNode renderNode)
                {
                    result.Add(renderNode);
                }
            }
        }

        return result;
    }
}
