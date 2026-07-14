using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Microsoft.Extensions.Logging;

namespace Beutl.NodeGraph;

internal class NodeGraphFilterEffectRenderNode(NodeGraphFilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    private static readonly ILogger s_logger = Log.CreateLogger<NodeGraphFilterEffectRenderNode>();

    private readonly CompositionContext _compositionContext = new(TimeSpan.Zero);

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
        try
        {
            // 3. グラフのノードを評価
            _compositionContext.Time = lastTime.Value;
            _compositionContext.PreferProxy = GraphResource.PreferProxy;
            _compositionContext.PreferredProxyPreset = GraphResource.PreferredProxyPreset;
            _compositionContext.DisableResourceShare = GraphResource.DisableResourceShare;
            GraphResource.Snapshot.Evaluate(CompositionTarget.Graphics, _compositionContext);

            // 4. OutputNode から出力 RenderNode を収集
            var outputRenderNodes = PullOutputValue(model);
            if (outputRenderNodes.Count == 0)
            {
                // SetOperations transferred the input into the wrapper's ref-counted ownership. Return proxies
                // before the finally block clears the wrapper: the proxies keep those refs alive until the caller
                // disposes the passthrough output, while the wrapper itself retains nothing after Process returns.
                return inputWrapper.Process(new RenderNodeContext([]));
            }

            // 5. RenderNodeProcessor でグラフ出力ツリーを処理
            foreach (RenderNode outputNode in outputRenderNodes)
            {
                // Thread the complete parent execution policy through the hosted graph.
                var processor = new RenderNodeProcessor(
                    outputNode, context.IsRenderCacheEnabled, context.OutputScale, context.MaxWorkingScale,
                    context.Diagnostics, context.Pool)
                {
                    RequestedBounds = context.RequestedBounds,
                    IsAuxiliaryPull = context.IsAuxiliaryPull,
                };
                allResults.AddRange(processor.PullToRoot());
            }

            return allResults.ToArray();
        }
        catch
        {
            RenderNodeOperation.DisposeAll(CollectionsMarshal.AsSpan(allResults));
            throw;
        }
        finally
        {
            try
            {
                inputWrapper.SetOperations([]);
            }
            catch (Exception ex)
            {
                // SetOperations publishes the empty wrapper and sweeps every reference before surfacing a cleanup
                // failure. The graph outputs are already complete (or a primary pull failure is already in flight),
                // so teardown must not make those outputs unreachable or replace that primary exception.
                s_logger.LogWarning(ex, "A node-graph input operation failed to dispose after graph evaluation");
            }
        }
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
