using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderRequestResourceRegistry : IDisposable
{
    private readonly Dictionary<object, List<RenderResourceRegistration>> _slotsByRawValue =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<object, OwnedResourceTombstone> _ownedTombstones = new();
    private readonly ConditionalWeakTable<object, BorrowedResourceTombstone> _borrowedTombstones = new();
    private readonly List<RenderResourceRegistration> _slots = [];
    private bool _disposed;

    public RenderResource<T> RegisterOwned<T>(T value, IRenderResourceRecordingScope? scope = null)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();

        if (_ownedTombstones.TryGetValue(value, out _))
        {
            throw new InvalidOperationException(
                "The raw resource was already transferred to this request family and cannot be registered again.");
        }

        if (_borrowedTombstones.TryGetValue(value, out _))
        {
            throw new InvalidOperationException(
                "The raw resource was already borrowed by this request family and cannot later transfer ownership.");
        }

        if (_slotsByRawValue.TryGetValue(value, out List<RenderResourceRegistration>? registrations)
            && registrations.Count > 0)
        {
            throw new InvalidOperationException(
                "The raw resource is already registered. Duplicate ownership and Own/Borrow mixtures are forbidden.");
        }

        RenderResourceRegistration slot = CreateSlot(
            value,
            RenderResourceOwnershipMode.Owned);
        _ownedTombstones.Add(value, OwnedResourceTombstone.Instance);
        return CreateToken<T>(slot, scope);
    }

    public RenderResource<T> RegisterBorrowed<T>(T value, IRenderResourceRecordingScope? scope = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();

        if (_ownedTombstones.TryGetValue(value, out _))
        {
            throw new InvalidOperationException(
                "The raw resource was already transferred to this request family and cannot be borrowed.");
        }

        if (_slotsByRawValue.TryGetValue(value, out List<RenderResourceRegistration>? registrations))
        {
            if (registrations.Any(static slot => slot.Mode == RenderResourceOwnershipMode.Owned))
            {
                throw new InvalidOperationException(
                    "The raw resource is already owned by this request family and cannot also be borrowed.");
            }
        }

        RenderResourceRegistration created = CreateSlot(
            value,
            RenderResourceOwnershipMode.Borrowed);
        RenderResource<T> createdToken = CreateToken<T>(created, scope);
        MarkBorrowed(value);
        return createdToken;
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
        resource.Slot.PendingRegistrations--;
        resource.Slot.CommittedRegistrations++;
        resource.Slot.UpdateStableState();
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
        RenderResourceRegistration slot = resource.Slot;
        RenderResourceOwnershipState returnState = slot.State;
        bool ownsLease = returnState != RenderResourceOwnershipState.LeasedToCallback;
        if (ownsLease)
        {
            slot.State = RenderResourceOwnershipState.LeasedToCallback;
        }

        try
        {
            return use(slot.RawValue);
        }
        finally
        {
            if (ownsLease && slot.State == RenderResourceOwnershipState.LeasedToCallback)
            {
                slot.State = returnState;
            }
        }
    }

    public T TransferOwned<T>(RenderResource<T> resource)
        where T : class, IDisposable
    {
        EnsureCommitted(resource);
        RenderResourceRegistration slot = resource.Slot;
        if (slot.Mode != RenderResourceOwnershipMode.Owned
            || slot.State != RenderResourceOwnershipState.RequestOwned)
        {
            throw new InvalidOperationException("Only an unleased request-owned resource can transfer to a cache.");
        }

        slot.State = RenderResourceOwnershipState.Discharged;
        InvalidateTokens(slot);
        RemoveSlot(slot);
        return (T)slot.TakeRawValue();
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
        for (int index = _slots.Count - 1; index >= 0; index--)
        {
            RenderResourceRegistration slot = _slots[index];
            try
            {
                RemoveSlot(slot);
                DischargeSlot(slot);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        _slots.Clear();
        _slotsByRawValue.Clear();
        _ownedTombstones.Clear();
        _borrowedTombstones.Clear();
        if (failures is not null)
        {
            throw new AggregateException("One or more render resources failed to discharge.", failures);
        }
    }

    internal IReadOnlyList<RenderResourceRegistration> Slots => _slots;

    internal void ValidateBinding(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
            throw new InvalidOperationException("A released render resource cannot be bound to a resource slot.");
    }

    private RenderResourceRegistration CreateSlot(
        object rawValue,
        RenderResourceOwnershipMode mode)
    {
        var slot = new RenderResourceRegistration(rawValue, mode);
        _slots.Add(slot);
        if (!_slotsByRawValue.TryGetValue(rawValue, out List<RenderResourceRegistration>? registrations))
        {
            registrations = [];
            _slotsByRawValue.Add(rawValue, registrations);
        }

        registrations.Add(slot);
        return slot;
    }

    private RenderResource<T> CreateToken<T>(
        RenderResourceRegistration slot,
        IRenderResourceRecordingScope? scope)
        where T : class
    {
        var token = new RenderResource<T>(this, slot)
        {
            RegistrationState = RenderResourceRegistrationState.Pending,
            RecordingScope = scope,
        };
        slot.PendingRegistrations++;
        slot.Tokens.Add(token);
        slot.UpdateStableState();
        return token;
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
        RenderResourceRegistration slot = resource.Slot;
        if (slot.State == RenderResourceOwnershipState.LeasedToCallback)
        {
            throw new InvalidOperationException(
                "A leased render resource cannot be released from its callback.");
        }

        switch (resource.RegistrationState)
        {
            case RenderResourceRegistrationState.Pending:
                slot.PendingRegistrations--;
                break;
            case RenderResourceRegistrationState.Committed:
                slot.CommittedRegistrations--;
                break;
            default:
                return;
        }

        resource.RegistrationState = RenderResourceRegistrationState.Released;
        resource.RecordingScope = null;
        if (slot.PendingRegistrations == 0 && slot.CommittedRegistrations == 0)
        {
            RemoveSlot(slot);
            DischargeSlot(slot);
        }
        else
        {
            slot.UpdateStableState();
            resource.Detach(RenderResourceOwnershipState.ReleasedToken);
        }
    }

    private static void DischargeSlot(RenderResourceRegistration slot)
    {
        if (slot.State is RenderResourceOwnershipState.Discharged
            or RenderResourceOwnershipState.ReleasedToken)
        {
            return;
        }

        if (slot.State == RenderResourceOwnershipState.LeasedToCallback)
        {
            throw new InvalidOperationException("A leased render resource cannot be discharged.");
        }

        if (slot.Mode == RenderResourceOwnershipMode.Owned)
        {
            slot.State = RenderResourceOwnershipState.Discharged;
            InvalidateTokens(slot);
            ((IDisposable)slot.TakeRawValue()).Dispose();
        }
        else
        {
            slot.State = RenderResourceOwnershipState.ReleasedToken;
            InvalidateTokens(slot);
            _ = slot.TakeRawValue();
        }
    }

    private static void InvalidateTokens(RenderResourceRegistration slot)
    {
        foreach (RenderResource token in slot.Tokens)
        {
            token.RegistrationState = RenderResourceRegistrationState.Released;
            token.RecordingScope = null;
            token.Detach(slot.State);
        }

        slot.PendingRegistrations = 0;
        slot.CommittedRegistrations = 0;
    }

    private void RemoveSlot(RenderResourceRegistration slot)
    {
        _slots.Remove(slot);
        object rawValue = slot.RawValue;
        if (_slotsByRawValue.TryGetValue(rawValue, out List<RenderResourceRegistration>? registrations))
        {
            registrations.Remove(slot);
            if (registrations.Count == 0)
            {
                _slotsByRawValue.Remove(rawValue);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void MarkBorrowed(object value)
        => _ = _borrowedTombstones.GetValue(value, static _ => BorrowedResourceTombstone.Instance);
}
