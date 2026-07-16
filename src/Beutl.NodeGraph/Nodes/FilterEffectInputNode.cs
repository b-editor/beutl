using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public partial class FilterEffectInputNode : GraphNode
{
    public FilterEffectInputNode()
    {
        Output = AddOutput<RenderNode?>("Output");
    }

    public OutputPort<RenderNode?> Output { get; }

    public partial class Resource
    {
        private OperationWrapperRenderNode? _wrapper = new();

        internal OperationWrapperRenderNode Wrapper
            => ReadGeneratedResourceState(ref _wrapper)
                ?? throw new ObjectDisposedException(nameof(Resource));

        protected override void UpdateCore(GraphCompositionContext context)
        {
            Output = Wrapper;
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            OperationWrapperRenderNode? wrapper = _wrapper;
            _wrapper = null;
            Output = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, wrapper);
            ThrowIfCleanupFailed(failure);
        }
    }
}
