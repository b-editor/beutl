using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderRequestResourceRegistry : IDisposable
{
    private static readonly object s_ownedTombstone = new();
    private static readonly object s_borrowedTombstone = new();
    private readonly ConditionalWeakTable<object, object> _tombstones = new();
    private readonly List<RenderResource> _resources = [];
    private bool _disposed;

    public RenderResource<T> RegisterOwned<T>(T value, IRenderResourceRecordingScope? scope = null)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();

        if (_tombstones.TryGetValue(value, out object? tombstone))
        {
            string message = ReferenceEquals(tombstone, s_ownedTombstone)
                ? "The raw resource was already transferred to this request family and cannot be registered again."
                : "The raw resource was already borrowed by this request family and cannot later transfer ownership.";
            throw new InvalidOperationException(message);
        }

        RenderResource<T> resource = CreateResource(
            value,
            RenderResourceOwnershipMode.Owned,
            scope);
        _tombstones.Add(value, s_ownedTombstone);
        return resource;
    }

    public RenderResource<T> RegisterBorrowed<T>(T value, IRenderResourceRecordingScope? scope = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();

        if (_tombstones.TryGetValue(value, out object? tombstone)
            && ReferenceEquals(tombstone, s_ownedTombstone))
        {
            throw new InvalidOperationException(
                "The raw resource was already transferred to this request family and cannot be borrowed.");
        }

        RenderResource<T> resource = CreateResource(
            value,
            RenderResourceOwnershipMode.Borrowed,
            scope);
        MarkBorrowed(value);
        return resource;
    }

    public void Commit(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState != RenderResourceRegistrationState.Pending)
        {
            throw new InvalidOperationException("Only a pending resource registration can be committed.");
        }

        resource.RegistrationState = RenderResourceRegistrationState.Committed;
        resource.RecordingScope = null;
        resource.OwnershipState = resource.Mode == RenderResourceOwnershipMode.Owned
            ? RenderResourceOwnershipState.RequestOwned
            : RenderResourceOwnershipState.RequestBorrowed;
    }

    public void Rollback(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
        {
            return;
        }

        if (resource.RegistrationState != RenderResourceRegistrationState.Pending)
        {
            throw new InvalidOperationException("Only a pending resource registration can be rolled back.");
        }

        ReleaseCore(resource);
    }

    public TResult Use<T, TResult>(RenderResource<T> resource, Func<T, TResult> use)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(use);
        return UseUntyped(resource, value => use((T)value));
    }

    internal TResult UseUntyped<TResult>(RenderResource resource, Func<object, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(use);
        EnsureReadable(resource);

        // A lease is a read, and one composable operation can reach the same declared resource twice: a
        // scope that binds a token and replays an input that binds the same one runs the inner read inside
        // the outer lease. Refusing that made two operations uncomposable for sharing a resource, so the
        // outermost lease owns the state and an inner one just reads through it.
        RenderResourceOwnershipState returnState = resource.OwnershipState;
        bool ownsLease = returnState != RenderResourceOwnershipState.LeasedToCallback;
        if (ownsLease)
        {
            resource.OwnershipState = RenderResourceOwnershipState.LeasedToCallback;
        }

        try
        {
            return use(resource.RawValue);
        }
        finally
        {
            if (ownsLease && resource.OwnershipState == RenderResourceOwnershipState.LeasedToCallback)
            {
                resource.OwnershipState = returnState;
            }
        }
    }

    public T TransferOwned<T>(RenderResource<T> resource)
        where T : class, IDisposable
    {
        EnsureCommitted(resource);
        if (resource.Mode != RenderResourceOwnershipMode.Owned
            || resource.OwnershipState != RenderResourceOwnershipState.RequestOwned)
        {
            throw new InvalidOperationException("Only an unleased request-owned resource can transfer to a cache.");
        }

        RemoveResource(resource);
        return (T)resource.Detach(RenderResourceOwnershipState.Discharged);
    }

    public void Release(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
        {
            return;
        }

        ReleaseCore(resource);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;
        for (int index = _resources.Count - 1; index >= 0; index--)
        {
            RenderResource resource = _resources[index];
            try
            {
                _resources.RemoveAt(index);
                DischargeResource(resource);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        _resources.Clear();
        _tombstones.Clear();
        if (failures is not null)
        {
            throw new AggregateException("One or more render resources failed to discharge.", failures);
        }
    }

    internal int ActiveResourceCount => _resources.Count;

    internal void ValidateBinding(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
            throw new InvalidOperationException("A released render resource cannot be bound to a resource slot.");
    }

    private RenderResource<T> CreateResource<T>(
        T rawValue,
        RenderResourceOwnershipMode mode,
        IRenderResourceRecordingScope? scope)
        where T : class
    {
        var resource = new RenderResource<T>(this, rawValue, mode)
        {
            RegistrationState = RenderResourceRegistrationState.Pending,
            RecordingScope = scope,
        };
        _resources.Add(resource);
        return resource;
    }

    private void EnsureRegistered(RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ThrowIfDisposed();
        if (!ReferenceEquals(resource.Registry, this))
        {
            throw new InvalidOperationException("The resource belongs to a different render request family.");
        }
    }

    private void EnsureCommitted(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState != RenderResourceRegistrationState.Committed)
        {
            throw new InvalidOperationException("The resource is not committed to this request.");
        }
    }

    // Reading is weaker than mutating: a recording has to be able to evaluate what it just registered -
    // a recording-time hit test over a bound resource - and nothing commits before the recording ends.
    // A pending registration can still be rolled back, so only the recording that owns it may read it;
    // for anyone else, and for everyone once that recording has ended, a commit is still required.
    private void EnsureReadable(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState == RenderResourceRegistrationState.Committed
            || (resource.RegistrationState == RenderResourceRegistrationState.Pending
                && resource.RecordingScope?.IsRecording == true))
        {
            return;
        }

        throw new InvalidOperationException("The resource is not committed to this request.");
    }

    private void ReleaseCore(RenderResource resource)
    {
        if (resource.OwnershipState == RenderResourceOwnershipState.LeasedToCallback)
        {
            throw new InvalidOperationException(
                "A leased render resource cannot be released from its callback.");
        }

        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
            return;

        RemoveResource(resource);
        DischargeResource(resource);
    }

    private static void DischargeResource(RenderResource resource)
    {
        if (resource.OwnershipState is RenderResourceOwnershipState.Discharged
            or RenderResourceOwnershipState.ReleasedToken)
        {
            return;
        }

        if (resource.OwnershipState == RenderResourceOwnershipState.LeasedToCallback)
        {
            throw new InvalidOperationException("A leased render resource cannot be discharged.");
        }

        if (resource.Mode == RenderResourceOwnershipMode.Owned)
        {
            ((IDisposable)resource.Detach(RenderResourceOwnershipState.Discharged)).Dispose();
        }
        else
        {
            _ = resource.Detach(RenderResourceOwnershipState.ReleasedToken);
        }
    }

    private void RemoveResource(RenderResource resource)
    {
        if (!_resources.Remove(resource))
        {
            throw new InvalidOperationException("The render resource is not active in this request family.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void MarkBorrowed(object value)
        => _ = _tombstones.GetValue(value, static _ => s_borrowedTombstone);
}
