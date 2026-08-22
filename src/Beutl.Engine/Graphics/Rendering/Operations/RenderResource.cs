using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Represents a declaration-owned resource address.
/// </summary>
/// <remarks>
/// This non-generic base exists only so a definition can declare a heterogeneous set of typed slots.
/// It does not expose a raw resource type or value to callbacks.
/// </remarks>
public abstract class RenderResourceSlot
{
    internal RenderResourceSlot()
    {
    }

    internal abstract Type ValueType { get; }

    internal abstract bool Accepts(RenderResource resource);
}

/// <summary>
/// Declares one typed resource address for a reusable render definition.
/// </summary>
/// <typeparam name="T">The raw resource type leased to the execution callback.</typeparam>
public sealed class RenderResourceSlot<T> : RenderResourceSlot
    where T : class
{
    /// <summary>Initializes a resource slot.</summary>
    public RenderResourceSlot()
    {
    }

    /// <summary>Binds this declared slot to a resource token from the active render context.</summary>
    /// <param name="resource">The request-scoped resource token to bind.</param>
    /// <returns>A binding suitable for a call of the definition that declares this slot.</returns>
    public RenderResourceBinding Bind(RenderResource<T> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.Registry.ValidateBinding(resource);
        return new RenderResourceBinding(this, resource);
    }

    internal override Type ValueType => typeof(T);

    internal override bool Accepts(RenderResource resource)
        => resource is RenderResource<T>;
}

/// <summary>
/// Binds a definition-declared resource slot to a request-scoped resource token.
/// </summary>
/// <remarks>
/// Bindings can only be created by <see cref="RenderResourceSlot{T}.Bind(RenderResource{T})"/>, which
/// prevents pairing a slot with a fabricated or differently typed token.
/// </remarks>
public sealed class RenderResourceBinding
{
    internal RenderResourceBinding(RenderResourceSlot slot, RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(resource);
        if (!slot.Accepts(resource))
        {
            throw new ArgumentException(
                "A render resource binding must use a token whose type matches its slot.",
                nameof(resource));
        }

        Slot = slot;
        Resource = resource;
    }

    internal RenderResourceSlot Slot { get; }

    internal RenderResource Resource { get; }

    internal static RenderResourceBinding CreateEngineBinding(RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.Registry.ValidateBinding(resource);
        return new RenderResourceBinding(new EngineRenderResourceSlot(resource.ValueType), resource);
    }
}

/// <summary>
/// Identifies a request-scoped resource without exposing its raw value.
/// </summary>
public abstract class RenderResource
{
    private RenderResourceRegistration? _slot;
    private RenderResourceOwnershipState _terminalState;

    internal RenderResource(RenderRequestResourceRegistry registry, RenderResourceRegistration slot)
    {
        Registry = registry;
        _slot = slot;
    }

    internal RenderRequestResourceRegistry Registry { get; }

    internal abstract Type ValueType { get; }

    internal RenderResourceRegistration Slot => GetActiveSlot();

    internal object SlotIdentity => GetActiveSlot();

    internal RenderResourceOwnershipState OwnershipState => _slot?.State ?? _terminalState;

    internal RenderResourceRegistrationState RegistrationState { get; set; }

    internal void Detach(RenderResourceOwnershipState terminalState)
    {
        _terminalState = terminalState;
        _slot = null;
    }

    private RenderResourceRegistration GetActiveSlot()
        => _slot ?? throw new InvalidOperationException(
            "A released render resource no longer retains its request-scoped slot.");
}

/// <summary>
/// Identifies a typed request-scoped resource without publicly exposing its raw value.
/// </summary>
/// <typeparam name="T">The raw resource type.</typeparam>
public sealed class RenderResource<T> : RenderResource
    where T : class
{
    internal RenderResource(RenderRequestResourceRegistry registry, RenderResourceRegistration slot)
        : base(registry, slot)
    {
    }

    internal override Type ValueType => typeof(T);
}

internal sealed class EngineRenderResourceSlot(Type valueType) : RenderResourceSlot
{
    private readonly Type _valueType = valueType ?? throw new ArgumentNullException(nameof(valueType));

    internal override Type ValueType => _valueType;

    internal override bool Accepts(RenderResource resource) => true;
}

internal sealed class RenderRequestResourceRegistry : IDisposable
{
    private readonly Dictionary<object, List<RenderResourceRegistration>> _slotsByRawValue =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<object, OwnedResourceTombstone> _ownedTombstones = new();
    private readonly ConditionalWeakTable<object, BorrowedResourceTombstone> _borrowedTombstones = new();
    private readonly List<RenderResourceRegistration> _slots = [];
    private bool _disposed;

    public RenderResource<T> RegisterOwned<T>(T value)
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
        return CreateToken<T>(slot);
    }

    public RenderResource<T> RegisterBorrowed<T>(T value)
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
        RenderResource<T> createdToken = CreateToken<T>(created);
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
        EnsureCommitted(resource);

        RenderResourceRegistration slot = resource.Slot;
        if (slot.State == RenderResourceOwnershipState.LeasedToCallback)
        {
            throw new InvalidOperationException("A render resource cannot be leased by nested callbacks.");
        }

        RenderResourceOwnershipState returnState = slot.State;
        slot.State = RenderResourceOwnershipState.LeasedToCallback;
        try
        {
            return use(slot.RawValue);
        }
        finally
        {
            if (slot.State == RenderResourceOwnershipState.LeasedToCallback)
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

    private RenderResource<T> CreateToken<T>(RenderResourceRegistration slot)
        where T : class
    {
        var token = new RenderResource<T>(this, slot)
        {
            RegistrationState = RenderResourceRegistrationState.Pending,
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

internal sealed class RenderResourceRegistration
{
    private object? _rawValue;

    public RenderResourceRegistration(
        object rawValue,
        RenderResourceOwnershipMode mode)
    {
        _rawValue = rawValue;
        Mode = mode;
        State = mode == RenderResourceOwnershipMode.Owned
            ? RenderResourceOwnershipState.Pending
            : RenderResourceOwnershipState.BorrowedPending;
    }

    public object RawValue
        => _rawValue ?? throw new InvalidOperationException(
            "The render resource slot no longer retains its raw value.");

    public object TakeRawValue()
    {
        object value = RawValue;
        _rawValue = null;
        return value;
    }

    public RenderResourceOwnershipMode Mode { get; }

    public List<RenderResource> Tokens { get; } = [];

    public int PendingRegistrations { get; set; }

    public int CommittedRegistrations { get; set; }

    public RenderResourceOwnershipState State { get; set; }

    public void UpdateStableState()
    {
        if (State is RenderResourceOwnershipState.Discharged
            or RenderResourceOwnershipState.ReleasedToken
            or RenderResourceOwnershipState.LeasedToCallback)
        {
            return;
        }

        State = Mode switch
        {
            RenderResourceOwnershipMode.Owned when CommittedRegistrations > 0
                => RenderResourceOwnershipState.RequestOwned,
            RenderResourceOwnershipMode.Owned
                => RenderResourceOwnershipState.Pending,
            RenderResourceOwnershipMode.Borrowed when CommittedRegistrations > 0
                => RenderResourceOwnershipState.RequestBorrowed,
            _ => RenderResourceOwnershipState.BorrowedPending,
        };
    }
}

internal enum RenderResourceOwnershipMode : byte
{
    Owned,
    Borrowed,
}

internal enum RenderResourceOwnershipState : byte
{
    Pending,
    RequestOwned,
    BorrowedPending,
    RequestBorrowed,
    LeasedToCallback,
    Discharged,
    ReleasedToken,
}

internal enum RenderResourceRegistrationState : byte
{
    Pending,
    Committed,
    Released,
}

internal sealed class OwnedResourceTombstone
{
    public static OwnedResourceTombstone Instance { get; } = new();

    private OwnedResourceTombstone()
    {
    }
}

internal sealed class BorrowedResourceTombstone
{
    public static BorrowedResourceTombstone Instance { get; } = new();

    private BorrowedResourceTombstone()
    {
    }
}
