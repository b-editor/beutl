using Beutl.Collections.Pooled;
using Beutl.Composition;

namespace Beutl.Engine;

// Capture the inputs actually consumed by an operator. Re-evaluation must not
// consume the caller's remaining flow or substitute the operator's stored target.
internal sealed record FlowNode(EngineObject Object, IReadOnlyList<FlowNode> Inputs)
{
    public static FlowNode? Capture(EngineObject.Resource resource)
    {
        if (resource.GetOriginal() is not { } obj) return null;
        IReadOnlyList<FlowNode> inputs = resource.FlowInputs;
        FlowNode? captured = resource.CapturedFlow;
        // Playback parameters and Version can change every frame without changing
        // provenance. Only replace this immutable node when its graph changes.
        if (captured == null || !ReferenceEquals(captured.Object, obj) || !ReferenceEquals(captured.Inputs, inputs))
            resource.CapturedFlow = captured = new FlowNode(obj, inputs);
        return captured;
    }

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
        int count = 0;
        bool changedInputs = false;
        foreach (TResource resource in consumed)
        {
            if (FlowNode.Capture(resource) is not { } node) continue;
            if (count >= Inputs.Count || !ReferenceEquals(Inputs[count], node)) changedInputs = true;
            count++;
        }
        if (!changedInputs && count == Inputs.Count) return;
        if (count == 0)
        {
            Inputs = [];
            return;
        }
        var snapshot = new FlowNode[count];
        int index = 0;
        foreach (TResource resource in consumed)
            if (FlowNode.Capture(resource) is { } node) snapshot[index++] = node;
        Inputs = snapshot;
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
