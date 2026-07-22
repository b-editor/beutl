namespace Beutl.Graphics.Rendering;

internal sealed class RenderRequest : IDisposable
{
    private static long s_nextRequestId;
    private readonly List<RenderRequest> _children = [];

    public RenderRequest(RenderRequestOptions options, RenderRequest? parent = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (parent is not null && options.TargetBinding is null)
        {
            throw new ArgumentException(
                "A nested request requires a typed separate-target binding.",
                nameof(options));
        }

        if (parent is not null && !HasInheritedRequestPolicy(options, parent.Options))
        {
            throw new ArgumentException(
                "A nested request must be created from its parent options and inherit intent, purpose, cache, "
                + "fusion, owner, and diagnostic policy.",
                nameof(options));
        }

        long value = Interlocked.Increment(ref s_nextRequestId);
        if (value <= 0)
        {
            throw new InvalidOperationException("The render request ID space was exhausted.");
        }

        Id = new RenderRequestId(value);
        ParentId = parent?.Id;
        Options = options;
        State = RenderRequestState.Created;
        parent?.RegisterChild(this);
    }

    public RenderRequestId Id { get; }

    public RenderRequestId? ParentId { get; }

    public RenderRequestOptions Options { get; }

    public RenderRequestState State { get; private set; }

    public void TransitionTo(RenderRequestState next)
    {
        if (!Enum.IsDefined(next))
        {
            throw new ArgumentOutOfRangeException(nameof(next), next, "The request state is not defined.");
        }

        RenderRequestState expected = State switch
        {
            RenderRequestState.Created => RenderRequestState.Recording,
            RenderRequestState.Recording => RenderRequestState.Recorded,
            RenderRequestState.Recorded => RenderRequestState.TargetDependenciesLowered,
            RenderRequestState.TargetDependenciesLowered => RenderRequestState.MetadataResolved,
            RenderRequestState.MetadataResolved => RenderRequestState.RegionsResolved,
            RenderRequestState.RegionsResolved => RenderRequestState.CachesResolved,
            RenderRequestState.CachesResolved => RenderRequestState.Planned,
            RenderRequestState.Planned => RenderRequestState.Executing,
            RenderRequestState.Executing => RenderRequestState.Completed,
            _ => throw new InvalidOperationException($"Request state '{State}' cannot transition to another active state."),
        };

        if (next != expected)
        {
            throw new InvalidOperationException(
                $"Request state '{State}' must transition to '{expected}', not '{next}'.");
        }

        State = next;
    }

    public void CompleteMetadataOnly()
    {
        if (Options.Purpose is not (RenderRequestPurpose.Bounds or RenderRequestPurpose.HitTest))
        {
            throw new InvalidOperationException("Only Bounds and HitTest requests can complete without planning execution.");
        }

        if (State == RenderRequestState.Completed)
            return;

        if (State != RenderRequestState.MetadataResolved)
        {
            throw new InvalidOperationException("A metadata-only request completes after metadata resolution.");
        }

        State = RenderRequestState.Completed;
    }

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (State is RenderRequestState.Completed or RenderRequestState.Failed or RenderRequestState.Disposed)
        {
            throw new InvalidOperationException($"Request state '{State}' cannot fail.");
        }

        Options.Owner.RecordPrimaryFailure(exception);
        State = RenderRequestState.Failed;
        // Nested recorders share the family owner with their parent. Their failure must
        // unwind to the family root before owner cleanup starts, otherwise parent
        // transactions can observe their still-pending resources as prematurely disposed.
        if (ParentId is null)
            Options.Owner.Cleanup();
    }

    internal void FailAfterOwnerCleanup(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (State is RenderRequestState.Completed or RenderRequestState.Failed or RenderRequestState.Disposed)
        {
            throw new InvalidOperationException($"Request state '{State}' cannot fail.");
        }

        if (Options.Owner.PrimaryFailure is null)
            Options.Owner.RecordPrimaryFailure(exception);
        State = RenderRequestState.Failed;
        Options.Owner.Cleanup();
    }

    internal void FailFamilyMember()
    {
        if (State is RenderRequestState.Completed or RenderRequestState.Disposed)
        {
            throw new InvalidOperationException($"Request state '{State}' cannot fail with its request family.");
        }

        State = RenderRequestState.Failed;
    }

    public void Dispose()
    {
        if (State == RenderRequestState.Disposed)
        {
            return;
        }

        for (int index = _children.Count - 1; index >= 0; index--)
            _children[index].Dispose();

        if (Options.OwnsOwner)
        {
            Options.Owner.Cleanup();
        }

        State = RenderRequestState.Disposed;
    }

    private void RegisterChild(RenderRequest child)
    {
        if (State is RenderRequestState.Completed or RenderRequestState.Failed or RenderRequestState.Disposed)
        {
            throw new InvalidOperationException(
                $"Request state '{State}' cannot accept another nested request.");
        }

        _children.Add(child);
    }

    private static bool HasInheritedRequestPolicy(
        RenderRequestOptions nested,
        RenderRequestOptions parent)
    {
        return ReferenceEquals(nested.NestedPolicyParent, parent)
               && ReferenceEquals(nested.Owner, parent.Owner)
               && ReferenceEquals(nested.Diagnostics, parent.Diagnostics)
               && nested.Intent == parent.Intent
               && nested.Purpose == parent.Purpose
               && nested.CachePolicy == parent.CachePolicy
               && nested.FusionMode == parent.FusionMode;
    }
}

internal readonly record struct RenderRequestId
{
    public RenderRequestId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A render request ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }
}

internal enum RenderRequestState : byte
{
    Created,
    Recording,
    Recorded,
    TargetDependenciesLowered,
    MetadataResolved,
    RegionsResolved,
    CachesResolved,
    Planned,
    Executing,
    Completed,
    Failed,
    Disposed,
}
