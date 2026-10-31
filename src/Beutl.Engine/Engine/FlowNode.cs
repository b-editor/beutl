using Beutl.Collections.Pooled;
using Beutl.Composition;

namespace Beutl.Engine;

// Capture the inputs actually consumed by an operator. Re-evaluation must not
// consume the caller's remaining flow or substitute the operator's stored target.
internal sealed record FlowNode(EngineObject Object, IReadOnlyList<FlowNode> Inputs, EngineObject? FlowOwner = null)
{
    public bool IsVisibleAt(CompositionContext context)
    {
        var range = Object.TimeRange.Value;
        if (range.Duration <= MediaTime.Zero || range.Contains(context.Time)) return true;

        // A controller retimes its target's own clip content together. Portal inputs belong to another
        // clip even when their authored ranges happen to match, so they still follow the mapped clock.
        return context.TimeRemappedTarget is { } target
            && (ReferenceEquals(target, Object)
                || range == target.TimeRange.Value && ReferenceEquals(GetClipOwner(Object), GetClipOwner(target)));
    }

    private static IHierarchical GetClipOwner(EngineObject obj)
    {
        // Nested drawables inherit their range through their engine-object parents; the outer parent
        // owns the timeline clip. Keep unattached objects distinct rather than sharing a null owner.
        while (obj.FindHierarchicalParent<EngineObject>() is { } parent) obj = parent;
        return obj.HierarchicalParent ?? obj;
    }

    public static FlowNode? Capture(EngineObject.Resource resource)
    {
        if (resource.GetOriginal() is not { } obj) return null;
        IReadOnlyList<FlowNode> inputs = resource.FlowInputs;
        FlowNode? captured = resource.CapturedFlow;
        // Playback parameters and Version can change every frame without changing
        // provenance. Only replace this immutable node when its graph changes.
        if (captured == null || !ReferenceEquals(captured.Object, obj) || !ReferenceEquals(captured.Inputs, inputs)
            || !ReferenceEquals(captured.FlowOwner, resource.FlowOwner))
            resource.CapturedFlow = captured = new FlowNode(obj, inputs, resource.FlowOwner);
        return captured;
    }

    // The captured flow of each resource that has one, in order. Returns previous when it already holds exactly
    // those nodes, so an unchanged flow keeps its list and allocates nothing.
    public static IReadOnlyList<FlowNode> CaptureAll(
        IReadOnlyList<EngineObject.Resource> resources, IReadOnlyList<FlowNode> previous)
    {
        int count = 0;
        bool changed = false;
        for (int i = 0; i < resources.Count; i++)
        {
            if (Capture(resources[i]) is not { } node) continue;
            if (count >= previous.Count || !ReferenceEquals(previous[count], node)) changed = true;
            count++;
        }
        if (!changed && count == previous.Count) return previous;
        if (count == 0) return [];
        var snapshot = new FlowNode[count];
        int index = 0;
        for (int i = 0; i < resources.Count; i++)
            if (Capture(resources[i]) is { } node) snapshot[index++] = node;
        return snapshot;
    }

    internal static EngineObject? ConsumeOwner(CompositionContext context)
    {
        EngineObject? owner = context.Flow != null ? context.FlowOwner : null;
        // Subsequent children/properties see the remaining flow and must not replay this prefix again.
        context.FlowOwner = null;
        return owner;
    }

    public void Reconcile<TResource>(CompositionContext context, ref TResource? resource, ref bool changed)
        where TResource : EngineObject.Resource
    {
        var originalFlow = context.Flow;
        var originalReplay = context.ReplayedFlow;
        var originalOwner = context.FlowOwner;
        try
        {
            context.Flow = null;
            context.FlowOwner = null;
            context.ReplayedFlow = this;
            ResourceReconciler.ReconcileResource(context, Object, ref resource, ref changed);
        }
        finally
        {
            context.Flow = originalFlow;
            context.FlowOwner = originalOwner;
            context.ReplayedFlow = originalReplay;
        }
    }
}

// Flow resources normally belong to their producer. During a controller replay
// this state owns the copies, so a group can retain the same borrowed-prefix
// reconciliation and still release every re-evaluated resource exactly once.
internal sealed class FlowInputState : IDisposable
{
    private readonly List<EngineObject.Resource?> _owned = [];

    public IReadOnlyList<FlowNode> Inputs { get; private set; } = [];

    public EngineObject? FlowOwner { get; private set; }

    public void Collect<TResource>(CompositionContext context, EngineObject owner, PooledList<TResource> consumed)
        where TResource : EngineObject.Resource
    {
        if (context.ReplayedFlow is { } replay && ReferenceEquals(replay.Object, owner))
        {
            FlowOwner = replay.FlowOwner;
            Inputs = replay.Inputs;
            for (int i = 0; i < Inputs.Count; i++)
            {
                EngineObject.Resource? resource = i < _owned.Count ? _owned[i] : null;
                if (Inputs[i].IsVisibleAt(context))
                {
                    bool changed = false;
                    Inputs[i].Reconcile(context, ref resource, ref changed);
                    consumed.Add((TResource)resource!);
                }
                // Keep the slot when a sample crosses a clip edge, so seeking back reuses its resource.
                if (i < _owned.Count) _owned[i] = resource;
                else _owned.Add(resource);
            }
            while (_owned.Count > Inputs.Count)
            {
                _owned[^1]?.Dispose();
                _owned.RemoveAt(_owned.Count - 1);
            }
            return;
        }

        DisposeOwned();
        FlowOwner = FlowNode.ConsumeOwner(context);
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
        Inputs = FlowNode.CaptureAll(consumed, Inputs);
    }

    private void DisposeOwned()
    {
        foreach (var resource in _owned) resource?.Dispose();
        _owned.Clear();
    }

    public void Dispose()
    {
        DisposeOwned();
        Inputs = [];
    }
}
