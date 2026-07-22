using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Identifies a request-scoped resource without exposing its raw value.
/// </summary>
public abstract class RenderResource
{
    private RenderResourceSlot? _slot;
    private RenderResourceOwnershipState _terminalState;

    internal RenderResource(RenderRequestResourceRegistry registry, RenderResourceSlot slot)
    {
        Registry = registry;
        _slot = slot;
    }

    public RenderResourceIdentity CacheIdentity
    {
        get
        {
            Registry.ValidateIdentityAccess(this);
            return GetActiveSlot().CacheIdentity;
        }
    }

    internal RenderRequestResourceRegistry Registry { get; }

    internal RenderResourceSlot Slot => GetActiveSlot();

    internal object SlotIdentity => GetActiveSlot();

    internal RenderResourceOwnershipState OwnershipState => _slot?.State ?? _terminalState;

    internal RenderResourceRegistrationState RegistrationState { get; set; }

    internal void Detach(RenderResourceOwnershipState terminalState)
    {
        _terminalState = terminalState;
        _slot = null;
    }

    private RenderResourceSlot GetActiveSlot()
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
    internal RenderResource(RenderRequestResourceRegistry registry, RenderResourceSlot slot)
        : base(registry, slot)
    {
    }
}

public readonly record struct RenderResourceIdentity(object Key, long Version)
{
    internal void ThrowIfUninitialized(string parameterName)
    {
        if (Key is null)
        {
            throw new ArgumentException("A render resource identity must have a non-null key.", parameterName);
        }
    }
}

public readonly record struct RenderRuntimeIdentity(object Key)
{
    internal void ThrowIfUninitialized(string parameterName)
    {
        if (Key is null)
        {
            throw new ArgumentException("A render runtime identity must have a non-null key.", parameterName);
        }
    }
}

internal sealed class RenderRequestResourceRegistry : IDisposable
{
    private readonly Dictionary<object, List<RenderResourceSlot>> _slotsByRawValue =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<object, OwnedResourceTombstone> _ownedTombstones = new();
    private readonly List<RenderResourceSlot> _slots = [];
    private bool _disposed;

    public RenderResource<T> RegisterOwned<T>(T value, object? cacheKey = null, long version = 0)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();
        if (cacheKey is not null)
        {
            RenderIdentityKeyValidator.ThrowIfInvalid(cacheKey, nameof(cacheKey));
        }

        if (_ownedTombstones.TryGetValue(value, out _))
        {
            throw new InvalidOperationException(
                "The raw resource was already transferred to this request family and cannot be registered again.");
        }

        if (_slotsByRawValue.TryGetValue(value, out List<RenderResourceSlot>? registrations)
            && registrations.Count > 0)
        {
            throw new InvalidOperationException(
                "The raw resource is already registered. Duplicate ownership and Own/Borrow mixtures are forbidden.");
        }

        RenderResourceSlot slot = CreateSlot(
            value,
            RenderResourceOwnershipMode.Owned,
            CreateCacheIdentity(cacheKey, version),
            cacheKey is not null);
        _ownedTombstones.Add(value, OwnedResourceTombstone.Instance);
        return CreateToken<T>(slot);
    }

    public RenderResource<T> RegisterBorrowed<T>(T value, object? cacheKey = null, long version = 0)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();
        if (cacheKey is not null)
        {
            RenderIdentityKeyValidator.ThrowIfInvalid(cacheKey, nameof(cacheKey));
        }

        if (_ownedTombstones.TryGetValue(value, out _))
        {
            throw new InvalidOperationException(
                "The raw resource was already transferred to this request family and cannot be borrowed.");
        }

        if (_slotsByRawValue.TryGetValue(value, out List<RenderResourceSlot>? registrations))
        {
            if (registrations.Any(static slot => slot.Mode == RenderResourceOwnershipMode.Owned))
            {
                throw new InvalidOperationException(
                    "The raw resource is already owned by this request family and cannot also be borrowed.");
            }

            if (cacheKey is not null)
            {
                RenderResourceSlot? matching = null;
                foreach (RenderResourceSlot slot in registrations.Where(static item => item.HasExplicitCacheKey))
                {
                    if (slot.CacheIdentity.Version == version
                        && Equals(slot.CacheIdentity.Key, cacheKey))
                    {
                        matching = slot;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Repeated explicit Borrow registrations must use equal cache keys and matching versions.");
                    }
                }

                if (matching is not null)
                {
                    return CreateToken<T>(matching);
                }
            }
        }

        RenderResourceSlot created = CreateSlot(
            value,
            RenderResourceOwnershipMode.Borrowed,
            CreateCacheIdentity(cacheKey, version),
            cacheKey is not null);
        return CreateToken<T>(created);
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
        EnsureCommitted(resource);

        RenderResourceSlot slot = resource.Slot;
        if (slot.State == RenderResourceOwnershipState.LeasedToCallback)
        {
            throw new InvalidOperationException("A render resource cannot be leased by nested callbacks.");
        }

        RenderResourceOwnershipState returnState = slot.State;
        slot.State = RenderResourceOwnershipState.LeasedToCallback;
        try
        {
            return use((T)slot.RawValue);
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
        RenderResourceSlot slot = resource.Slot;
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
            RenderResourceSlot slot = _slots[index];
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
        if (failures is not null)
        {
            throw new AggregateException("One or more render resources failed to discharge.", failures);
        }
    }

    internal IReadOnlyList<RenderResourceSlot> Slots => _slots;

    internal void ValidateIdentityAccess(RenderResource resource)
    {
        EnsureRegistered(resource);
        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
            throw new InvalidOperationException("A released render resource has no usable cache identity.");
    }

    private RenderResourceSlot CreateSlot(
        object rawValue,
        RenderResourceOwnershipMode mode,
        RenderResourceIdentity cacheIdentity,
        bool hasExplicitCacheKey)
    {
        var slot = new RenderResourceSlot(rawValue, mode, cacheIdentity, hasExplicitCacheKey);
        _slots.Add(slot);
        if (!_slotsByRawValue.TryGetValue(rawValue, out List<RenderResourceSlot>? registrations))
        {
            registrations = [];
            _slotsByRawValue.Add(rawValue, registrations);
        }

        registrations.Add(slot);
        return slot;
    }

    private RenderResource<T> CreateToken<T>(RenderResourceSlot slot)
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

    private static RenderResourceIdentity CreateCacheIdentity(object? cacheKey, long version)
        => new(cacheKey ?? new RequestLocalResourceIdentityKey(), version);

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
        RenderResourceSlot slot = resource.Slot;
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

    private static void DischargeSlot(RenderResourceSlot slot)
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

    private static void InvalidateTokens(RenderResourceSlot slot)
    {
        foreach (RenderResource token in slot.Tokens)
        {
            token.RegistrationState = RenderResourceRegistrationState.Released;
            token.Detach(slot.State);
        }

        slot.PendingRegistrations = 0;
        slot.CommittedRegistrations = 0;
    }

    private void RemoveSlot(RenderResourceSlot slot)
    {
        _slots.Remove(slot);
        object rawValue = slot.RawValue;
        if (_slotsByRawValue.TryGetValue(rawValue, out List<RenderResourceSlot>? registrations))
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
}

internal sealed class RenderResourceSlot
{
    private object? _rawValue;

    public RenderResourceSlot(
        object rawValue,
        RenderResourceOwnershipMode mode,
        RenderResourceIdentity cacheIdentity,
        bool hasExplicitCacheKey)
    {
        _rawValue = rawValue;
        Mode = mode;
        CacheIdentity = cacheIdentity;
        HasExplicitCacheKey = hasExplicitCacheKey;
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

    public RenderResourceIdentity CacheIdentity { get; }

    public bool HasExplicitCacheKey { get; }

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

internal sealed class RequestLocalResourceIdentityKey
{
}

internal sealed class OwnedResourceTombstone
{
    public static OwnedResourceTombstone Instance { get; } = new();

    private OwnedResourceTombstone()
    {
    }
}
