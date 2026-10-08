using Beutl.Collections.Pooled;
using Beutl.Composition;

namespace Beutl.Engine;

// Capture the inputs actually consumed by an operator. Re-evaluation must not
// consume the caller's remaining flow or substitute the operator's stored target.
internal sealed record FlowNode(EngineObject Object, IReadOnlyList<FlowNode> Inputs)
{
    public static FlowNode? Capture(EngineObject.Resource resource)
        => resource.GetOriginal() is { } obj ? new(obj, resource.FlowInputs) : null;

    public void Reconcile<TResource>(CompositionContext context, ref TResource? resource, ref bool changed)
        where TResource : EngineObject.Resource
    {
        var originalFlow = context.Flow;
        var originalReplay = context.ReplayedFlow;
        try
        {
            context.Flow = null;
            context.ReplayedFlow = this;
            ResourceReconciler.ReconcileResource(context, Object, ref resource, ref changed);
        }
        finally
        {
            context.Flow = originalFlow;
            context.ReplayedFlow = originalReplay;
        }
    }
}

// Flow resources normally belong to their producer. During a controller replay
// this state owns the copies, so a group can retain the same borrowed-prefix
// reconciliation and still release every re-evaluated resource exactly once.
internal sealed class FlowInputState : IDisposable
{
    private readonly List<EngineObject.Resource> _owned = [];

    public IReadOnlyList<FlowNode> Inputs { get; private set; } = [];

    public void Collect<TResource>(CompositionContext context, EngineObject owner, PooledList<TResource> consumed)
        where TResource : EngineObject.Resource
    {
        if (context.ReplayedFlow is { } replay && ReferenceEquals(replay.Object, owner))
        {
            Inputs = replay.Inputs;
            for (int i = 0; i < Inputs.Count; i++)
            {
                EngineObject.Resource? resource = i < _owned.Count ? _owned[i] : null;
                bool changed = false;
                Inputs[i].Reconcile(context, ref resource, ref changed);
                if (i < _owned.Count) _owned[i] = resource!;
                else _owned.Add(resource!);
                consumed.Add((TResource)resource!);
            }
            while (_owned.Count > Inputs.Count)
            {
                _owned[^1].Dispose();
                _owned.RemoveAt(_owned.Count - 1);
            }
            return;
        }

        DisposeOwned();
        if (context.Flow != null)
        {
            for (int i = context.Flow.Count - 1; i >= 0; i--)
            {
                if (context.Flow[i] is TResource resource)
                {
                    consumed.Insert(0, resource);
                    context.Flow.RemoveAt(i);
                }
            }
        }
        Inputs = consumed.Select(resource => FlowNode.Capture(resource)).OfType<FlowNode>().ToArray();
    }

    private void DisposeOwned()
    {
        foreach (var resource in _owned) resource.Dispose();
        _owned.Clear();
    }

    public void Dispose()
    {
        DisposeOwned();
        Inputs = [];
    }
}
