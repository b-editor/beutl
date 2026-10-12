using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;

namespace Beutl.Graphics;

/// <summary>
/// The child drawables a container resource reconciles from its <c>Children</c> property and the flow, with the
/// versions and, when it replays its flow inputs, the inputs that reconciliation keeps.
/// </summary>
internal sealed class FlowChildren(bool keepsFlowInputs)
{
    private readonly PooledList<int> _versions = [];
    private readonly FlowInputState? _flowInputs = keepsFlowInputs ? new() : null;

    public List<Drawable.Resource> Items { get; set; } = [];

    public IReadOnlyList<FlowNode> FlowInputs => _flowInputs?.Inputs ?? [];

    /// <returns>Whether the children changed.</returns>
    public bool Reconcile(CompositionContext context, IListProperty<Drawable> property, EngineObject owner)
        => ResourceReconciler.ReconcileChildrenFromFlow(context, property, Items, _versions, owner, _flowInputs);

    public void Dispose()
    {
        ResourceReconciler.ReleaseReconciledChildren(Items, _versions);
        _flowInputs?.Dispose();
    }
}
