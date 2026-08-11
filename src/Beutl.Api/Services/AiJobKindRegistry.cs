using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Extensibility;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

/// <summary>
/// Owns a job-kind registration. The caller must retain this object for as long as the kind is
/// available and dispose it during unload. The registry keeps only a weak reference to this owner,
/// so an abandoned extension and its descriptor remain collectible. Disposing the registration
/// retires it, prevents new leases, and blocks until all leases already using its descriptor have
/// been released. An extension can therefore dispose descriptor-owned resources after this owner.
/// </summary>
public interface IAiJobKindRegistration : IDisposable
{
}

/// <summary>
/// Keeps a resolved descriptor alive for one synchronous or asynchronous operation. Consumers
/// must not cache the descriptor beyond this lease.
/// </summary>
public interface IAiJobKindLease : IDisposable
{
    AiJobKindDescriptor Descriptor { get; }
}

public interface IAiJobKindRegistry : IBeutlApiResource
{
    /// <summary>
    /// Registers a complete kind descriptor and returns its ownership object.
    /// </summary>
    IAiJobKindRegistration Register(
        AiJobKindDescriptor descriptor,
        AiJobKindRegistrationMode mode = AiJobKindRegistrationMode.Add);

    /// <summary>
    /// Acquires the currently active descriptor without making the registry a strong owner of
    /// extension-provided behavior.
    /// </summary>
    bool TryAcquire(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobKindLease? lease);

    AiJobStatusSemantics GetStatus(AiJob job);

    AiJobStatusSemantics GetStatus(AiJobKindId kind, AiJobStatusId status);
}

public sealed class AiJobKindRegistry : IAiJobKindRegistry, IDisposable
{
    private readonly Dictionary<AiJobKindId, List<WeakReference<Registration>>> _registrations = [];
    private readonly List<IAiJobKindRegistration> _ownedRegistrations = [];
    private readonly Dictionary<AiJobKindExtension, IAiJobKindRegistration> _extensionRegistrations
        = new(ReferenceEqualityComparer.Instance);
    private readonly object _extensionCompositionGate = new();
    private readonly object _gate = new();
    private static readonly ILogger s_logger = Log.CreateLogger<AiJobKindRegistry>();
    private IExtensionProvider? _extensionProvider;
    private volatile bool _disposed;

    public AiJobKindRegistry()
    {
    }

    public AiJobKindRegistry(IExtensionProvider extensionProvider)
    {
        AttachExtensionProvider(extensionProvider);
    }

    public static AiJobKindRegistry CreateBuiltIn(
        IAiImageGenerationService images,
        IAiVideoService videos,
        IAiEntitlementService entitlements,
        IExtensionProvider? extensionProvider = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(entitlements);

        var registry = new AiJobKindRegistry();
        foreach (AiJobKindDescriptor descriptor in BuiltInAiJobKinds.Create(images, videos, entitlements))
        {
            registry._ownedRegistrations.Add(registry.Register(descriptor));
        }

        if (extensionProvider is not null)
        {
            registry.AttachExtensionProvider(extensionProvider);
        }

        return registry;
    }

    public IAiJobKindRegistration Register(
        AiJobKindDescriptor descriptor,
        AiJobKindRegistrationMode mode = AiJobKindRegistrationMode.Add)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        AiJobKindId kind = Normalize(descriptor.Kind);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_registrations.TryGetValue(kind, out List<WeakReference<Registration>>? registrations))
            {
                registrations = [];
                _registrations.Add(kind, registrations);
            }

            RemoveCollectedRegistrations(registrations);
            bool exists = registrations.Count > 0;
            if (mode == AiJobKindRegistrationMode.Add && exists)
            {
                throw new ArgumentException(
                    $"AI job kind '{descriptor.Kind}' is already registered. Use Replace explicitly.",
                    nameof(descriptor));
            }
            if (mode == AiJobKindRegistrationMode.Replace && !exists)
            {
                throw new ArgumentException(
                    $"AI job kind '{descriptor.Kind}' cannot be replaced because it is not registered.",
                    nameof(descriptor));
            }

            var state = new RegistrationState(kind, descriptor);
            var registration = new Registration(this, state);
            registrations.Add(new WeakReference<Registration>(registration));
            return registration;
        }
    }

    public bool TryAcquire(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobKindLease? lease)
    {
        if (kind.Value.Length == 0)
        {
            lease = null;
            return false;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            AiJobKindId normalized = Normalize(kind);
            if (_registrations.TryGetValue(normalized, out List<WeakReference<Registration>>? registrations))
            {
                RemoveCollectedRegistrations(registrations);
                for (int index = registrations.Count - 1; index >= 0; index--)
                {
                    if (!registrations[index].TryGetTarget(out Registration? registration))
                    {
                        registrations.RemoveAt(index);
                    }
                    else if (registration.TryAcquire(out DescriptorLease? descriptorLease))
                    {
                        lease = descriptorLease;
                        return true;
                    }
                    else
                    {
                        registrations.RemoveAt(index);
                    }
                }

                if (registrations.Count == 0)
                {
                    _registrations.Remove(normalized);
                }
            }

            lease = null;
            return false;
        }
    }

    public AiJobStatusSemantics GetStatus(AiJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return GetStatus(job.Kind, job.Status);
    }

    public AiJobStatusSemantics GetStatus(AiJobKindId kind, AiJobStatusId status)
    {
        if (!TryAcquire(kind, out IAiJobKindLease? lease))
            return AiJobStatusSemantics.Unknown;

        using (lease)
        {
            return lease.Descriptor.StatusResolver.Resolve(status);
        }
    }

    public void Dispose()
    {
        IAiJobKindRegistration[] ownedRegistrations;
        IAiJobKindRegistration[] extensionRegistrations;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _registrations.Clear();
                ownedRegistrations = _ownedRegistrations.ToArray();
                _ownedRegistrations.Clear();
            }

            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensionProvider = null;
            }

            extensionRegistrations = _extensionRegistrations.Values.ToArray();
            _extensionRegistrations.Clear();

            foreach (IAiJobKindRegistration registration in extensionRegistrations)
            {
                registration.Dispose();
            }
        }

        foreach (IAiJobKindRegistration registration in ownedRegistrations)
        {
            registration.Dispose();
        }
    }

    private void AttachExtensionProvider(IExtensionProvider extensionProvider)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);
        lock (_extensionCompositionGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_extensionProvider is not null)
                throw new InvalidOperationException("An extension provider is already attached.");

            _extensionProvider = extensionProvider;
            extensionProvider.AllExtensions.CollectionChanged += OnExtensionsChanged;
            try
            {
                SynchronizeExtensionRegistrationsCore();
            }
            catch
            {
                extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensionProvider = null;
                throw;
            }
        }
    }

    private void OnExtensionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        lock (_extensionCompositionGate)
        {
            if (!_disposed)
            {
                SynchronizeExtensionRegistrationsCore();
            }
        }
    }

    private void SynchronizeExtensionRegistrationsCore()
    {
        IExtensionProvider extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException("No extension provider is attached.");
        AiJobKindExtension[] currentExtensions = extensionProvider.GetExtensions<AiJobKindExtension>();
        var currentSet = new HashSet<AiJobKindExtension>(
            currentExtensions,
            ReferenceEqualityComparer.Instance);

        IAiJobKindRegistration[] removedRegistrations = _extensionRegistrations
            .Where(pair => !currentSet.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToArray();
        foreach (AiJobKindExtension extension in _extensionRegistrations.Keys
                     .Where(extension => !currentSet.Contains(extension))
                     .ToArray())
        {
            _extensionRegistrations.Remove(extension);
        }

        foreach (IAiJobKindRegistration registration in removedRegistrations)
        {
            registration.Dispose();
        }

        foreach (AiJobKindExtension extension in currentExtensions)
        {
            if (_extensionRegistrations.ContainsKey(extension))
                continue;

            try
            {
                IAiJobKindRegistration registration = Register(
                    extension.Descriptor,
                    extension.RegistrationMode);
                _extensionRegistrations.Add(extension, registration);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(
                    ex,
                    "Could not register AI job kind contribution from {ExtensionType}.",
                    extension.GetType().FullName);
            }
        }
    }

    private void UnregisterAndDrain(Registration registration, RegistrationState state)
    {
        lock (_gate)
        {
            state.Retire();
            if (_registrations.TryGetValue(state.Kind, out List<WeakReference<Registration>>? registrations))
            {
                for (int index = registrations.Count - 1; index >= 0; index--)
                {
                    if (!registrations[index].TryGetTarget(out Registration? current)
                        || ReferenceEquals(current, registration))
                    {
                        registrations.RemoveAt(index);
                    }
                }

                if (registrations.Count == 0)
                {
                    _registrations.Remove(state.Kind);
                }
            }
        }

        state.WaitForLeaseDrain();
    }

    private static void RemoveCollectedRegistrations(
        List<WeakReference<Registration>> registrations)
    {
        for (int index = registrations.Count - 1; index >= 0; index--)
        {
            if (!registrations[index].TryGetTarget(out _))
            {
                registrations.RemoveAt(index);
            }
        }
    }

    private static AiJobKindId Normalize(AiJobKindId kind)
        => new(kind.Value.ToLowerInvariant());

    private sealed class RegistrationState(
        AiJobKindId kind,
        AiJobKindDescriptor descriptor)
    {
        private readonly object _gate = new();
        private int _activeLeases;
        private bool _retired;

        public AiJobKindId Kind { get; } = kind;

        public AiJobKindDescriptor Descriptor { get; } = descriptor;

        public bool TryAcquire([NotNullWhen(true)] out DescriptorLease? lease)
        {
            lock (_gate)
            {
                if (_retired)
                {
                    lease = null;
                    return false;
                }

                _activeLeases++;
                lease = new DescriptorLease(this);
                return true;
            }
        }

        public void Retire()
        {
            lock (_gate)
            {
                _retired = true;
            }
        }

        public void WaitForLeaseDrain()
        {
            lock (_gate)
            {
                while (_activeLeases > 0)
                {
                    Monitor.Wait(_gate);
                }
            }
        }

        public void ReleaseLease()
        {
            lock (_gate)
            {
                _activeLeases--;
                if (_activeLeases == 0)
                {
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }

    private sealed class Registration(
        AiJobKindRegistry registry,
        RegistrationState state) : IAiJobKindRegistration
    {
        private readonly object _disposeGate = new();
        private RegistrationOwner? _owner = new(registry, state);
        private bool _disposing;

        public bool TryAcquire([NotNullWhen(true)] out DescriptorLease? lease)
        {
            lock (_disposeGate)
            {
                if (_disposing || _owner is null)
                {
                    lease = null;
                    return false;
                }

                return _owner.State.TryAcquire(out lease);
            }
        }

        public void Dispose()
        {
            RegistrationOwner owner;
            lock (_disposeGate)
            {
                while (_disposing)
                {
                    Monitor.Wait(_disposeGate);
                }

                if (_owner is null)
                    return;

                _disposing = true;
                owner = _owner;
            }

            try
            {
                owner.Registry.UnregisterAndDrain(this, owner.State);
            }
            finally
            {
                lock (_disposeGate)
                {
                    _owner = null;
                    _disposing = false;
                    Monitor.PulseAll(_disposeGate);
                }
            }
        }

        private sealed record RegistrationOwner(
            AiJobKindRegistry Registry,
            RegistrationState State);
    }

    private sealed class DescriptorLease(RegistrationState state) : IAiJobKindLease
    {
        private RegistrationState? _state = state;

        public AiJobKindDescriptor Descriptor
            => Volatile.Read(ref _state)?.Descriptor
                ?? throw new ObjectDisposedException(nameof(IAiJobKindLease));

        public void Dispose()
        {
            RegistrationState? state = Interlocked.Exchange(ref _state, null);
            state?.ReleaseLease();
        }
    }
}
