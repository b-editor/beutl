using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Extensibility;

namespace Beutl.Editor.Services;

/// <summary>
/// Resolves source handlers from host and package contributions. Removing a contribution retires
/// it before package unload and waits for active materialization calls to finish.
/// </summary>
public sealed class ElementSourceHandlerRegistry : IElementSourceHandlerRegistry, IDisposable
{
    private readonly Dictionary<Type, List<Registration>> _handlers = [];
    private readonly Dictionary<ElementSourceHandlerExtension, List<IDisposable>> _extensionRegistrations =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _extensionCompositionGate = new();
    private readonly object _gate = new();
    private readonly Action<ElementSourceHandlerExtensionFailure>? _reportFailure;
    private IExtensionProvider? _extensionProvider;
    private long _registrationSequence;
    private bool _disposed;

    public ElementSourceHandlerRegistry()
        : this([])
    {
    }

    public ElementSourceHandlerRegistry(
        IEnumerable<ElementSourceHandlerRegistration> hostRegistrations,
        IExtensionProvider? extensionProvider = null,
        Action<ElementSourceHandlerExtensionFailure>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(hostRegistrations);
        _reportFailure = reportFailure;
        foreach (ElementSourceHandlerRegistration registration in hostRegistrations)
        {
            Register(registration);
        }

        if (extensionProvider is not null)
        {
            AttachExtensionProvider(extensionProvider);
        }
    }

    public IReadOnlyList<IElementSourceHandler> Handlers
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _handlers
                    .Select(pair => pair.Value[^1].State)
                    .OrderBy(state => state.Order)
                    .ThenBy(state => state.SourceType.FullName, StringComparer.Ordinal)
                    .ThenBy(state => state.Sequence)
                    .Select(state => state.Handler)
                    .ToArray();
            }
        }
    }

    public IElementSourceHandlerRegistration Register(ElementSourceHandlerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        IElementSourceHandler handler = registration.Handler;
        ArgumentNullException.ThrowIfNull(handler);
        Type sourceType = handler.SourceType
            ?? throw new ArgumentException("A source handler must declare its source type.", nameof(handler));
        if (!typeof(Models.ElementSource).IsAssignableFrom(sourceType))
        {
            throw new ArgumentException(
                $"Source type '{sourceType.FullName}' does not derive from ElementSource.",
                nameof(handler));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool exists = _handlers.TryGetValue(sourceType, out List<Registration>? entries)
                && entries.Count > 0;
            if (registration.Mode == ElementSourceHandlerRegistrationMode.Add && exists)
            {
                throw new ArgumentException(
                    $"A handler for element source '{sourceType.FullName}' is already registered. "
                    + "Use Replace explicitly.",
                    nameof(registration));
            }
            if (registration.Mode == ElementSourceHandlerRegistrationMode.Replace && !exists)
            {
                throw new ArgumentException(
                    $"A handler for element source '{sourceType.FullName}' cannot be replaced "
                    + "because it is not registered.",
                    nameof(registration));
            }

            entries ??= [];
            _handlers[sourceType] = entries;
            var state = new RegistrationState(
                sourceType,
                handler,
                registration.Order,
                ++_registrationSequence);
            var registrationOwner = new Registration(this, state);
            entries.Add(registrationOwner);
            return registrationOwner;
        }
    }

    public bool TryAcquire(
        Type sourceType,
        [NotNullWhen(true)] out IElementSourceHandlerLease? lease)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handlers.TryGetValue(sourceType, out List<Registration>? entries))
            {
                for (int index = entries.Count - 1; index >= 0; index--)
                {
                    Registration registration = entries[index];
                    if (registration.TryAcquire(out HandlerLease? handlerLease))
                    {
                        lease = handlerLease;
                        return true;
                    }

                    entries.RemoveAt(index);
                }

                _handlers.Remove(sourceType);
            }

            lease = null;
            return false;
        }
    }

    public void Dispose()
    {
        Registration[] registrations;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                registrations = _handlers.Values.SelectMany(value => value).ToArray();
                _handlers.Clear();
            }

            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensionProvider = null;
            }

            _extensionRegistrations.Clear();
        }

        foreach (Registration registration in registrations)
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
        ElementSourceHandlerExtension[] currentExtensions =
            extensionProvider.GetExtensions<ElementSourceHandlerExtension>();
        var currentSet = new HashSet<ElementSourceHandlerExtension>(
            currentExtensions,
            ReferenceEqualityComparer.Instance);

        List<IDisposable>[] removedRegistrations = _extensionRegistrations
            .Where(pair => !currentSet.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToArray();
        foreach (ElementSourceHandlerExtension extension in _extensionRegistrations.Keys
                     .Where(extension => !currentSet.Contains(extension))
                     .ToArray())
        {
            _extensionRegistrations.Remove(extension);
        }

        foreach (List<IDisposable> registrations in removedRegistrations)
        {
            foreach (IDisposable registration in registrations)
            {
                registration.Dispose();
            }
        }

        foreach (ElementSourceHandlerExtension extension in currentExtensions)
        {
            if (_extensionRegistrations.ContainsKey(extension))
                continue;

            var registrations = new List<IDisposable>();
            try
            {
                foreach (ElementSourceHandlerRegistration registration in ValidateRegistrations(extension))
                {
                    registrations.Add(Register(registration));
                }

                _extensionRegistrations.Add(extension, registrations);
            }
            catch (Exception ex)
            {
                foreach (IDisposable registration in registrations)
                {
                    registration.Dispose();
                }

                ReportFailure(extension, ex);
            }
        }
    }

    private void UnregisterAndDrain(Registration registration, RegistrationState state)
    {
        lock (_gate)
        {
            state.Retire();
            if (_handlers.TryGetValue(state.SourceType, out List<Registration>? entries))
            {
                entries.Remove(registration);
                if (entries.Count == 0)
                {
                    _handlers.Remove(state.SourceType);
                }
            }
        }

        state.WaitForLeaseDrain();
    }

    private static ElementSourceHandlerRegistration[] ValidateRegistrations(
        ElementSourceHandlerExtension extension)
    {
        IReadOnlyCollection<ElementSourceHandlerRegistration>? registrations = extension.Registrations;
        if (registrations is null)
        {
            throw new InvalidOperationException(
                "An element source-handler extension returned a null registration collection.");
        }

        ElementSourceHandlerRegistration[] snapshot = registrations.ToArray();
        if (snapshot.Any(registration => registration is null))
        {
            throw new InvalidOperationException(
                "An element source-handler extension returned a null registration.");
        }

        return snapshot;
    }

    private void ReportFailure(ElementSourceHandlerExtension extension, Exception exception)
    {
        if (_reportFailure is null)
            return;

        try
        {
            _reportFailure(new ElementSourceHandlerExtensionFailure(
                extension.GetType().FullName ?? extension.GetType().Name,
                exception));
        }
        catch
        {
            // Diagnostics must not interrupt extension removal before Extension.Unload().
        }
    }

    private sealed class RegistrationState(
        Type sourceType,
        IElementSourceHandler handler,
        int order,
        long sequence)
    {
        private readonly object _gate = new();
        private int _activeLeases;
        private bool _retired;

        public Type SourceType { get; } = sourceType;

        public IElementSourceHandler Handler { get; } = handler;

        public int Order { get; } = order;

        public long Sequence { get; } = sequence;

        public bool TryAcquire([NotNullWhen(true)] out HandlerLease? lease)
        {
            lock (_gate)
            {
                if (_retired)
                {
                    lease = null;
                    return false;
                }

                _activeLeases++;
                lease = new HandlerLease(this);
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
        ElementSourceHandlerRegistry owner,
        RegistrationState state) : IElementSourceHandlerRegistration
    {
        private readonly object _disposeGate = new();
        private RegistrationOwner? _owner = new(owner, state);
        private bool _disposing;

        public RegistrationState State => _owner?.State
            ?? throw new ObjectDisposedException(nameof(IElementSourceHandlerRegistration));

        public bool TryAcquire([NotNullWhen(true)] out HandlerLease? lease)
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
            ElementSourceHandlerRegistry Registry,
            RegistrationState State);
    }

    private sealed class HandlerLease(RegistrationState state) : IElementSourceHandlerLease
    {
        private RegistrationState? _state = state;

        public IElementSourceHandler Handler
            => Volatile.Read(ref _state)?.Handler
                ?? throw new ObjectDisposedException(nameof(IElementSourceHandlerLease));

        public void Dispose()
            => Interlocked.Exchange(ref _state, null)?.ReleaseLease();
    }
}
