using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services;

/// <summary>
/// Resolves source handlers from host and package contributions. Removing a contribution retires
/// it before package unload and waits for active materialization calls to finish.
/// </summary>
public sealed class ElementSourceHandlerRegistry : IElementSourceHandlerRegistry, IAsyncDisposable
{
    private readonly Dictionary<Type, List<Registration>> _handlers = [];
    private readonly Dictionary<ElementSourceHandlerExtension, List<Registration>> _extensionRegistrations =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _extensionCompositionGate = new();
    private readonly object _gate = new();
    private readonly Action<ElementSourceHandlerExtensionFailure>? _reportFailure;
    private IExtensionProvider? _extensionProvider;
    private Task? _disposeTask;
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

    public ValueTask DisposeAsync()
    {
        if (_extensionProvider is IExtensionRegistry registry)
        {
            ValueTask result = default;
            registry.SynchronizeMutation(() => result = StartDispose());
            return result;
        }

        return StartDispose();
    }

    private ValueTask StartDispose()
    {
        lock (_extensionCompositionGate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private Task DisposeCoreAsync()
    {
        Registration[] registrations;
        KeyValuePair<ElementSourceHandlerExtension, List<Registration>>[] extensionRegistrations;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return Task.CompletedTask;

                _disposed = true;
                registrations = _handlers.Values.SelectMany(value => value).ToArray();
                _handlers.Clear();
            }

            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensionProvider = null;
            }

            extensionRegistrations = _extensionRegistrations.ToArray();
            _extensionRegistrations.Clear();
        }

        foreach ((ElementSourceHandlerExtension extension, List<Registration> owned)
                 in extensionRegistrations)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => DisposeRegistrationsAsync(owned));
        }

        return DisposeRegistrationsAsync(registrations).AsTask();
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

        KeyValuePair<ElementSourceHandlerExtension, List<Registration>>[] removedRegistrations =
            _extensionRegistrations
            .Where(pair => !currentSet.Contains(pair.Key))
            .ToArray();
        foreach (ElementSourceHandlerExtension extension in _extensionRegistrations.Keys
                     .Where(extension => !currentSet.Contains(extension))
                     .ToArray())
        {
            _extensionRegistrations.Remove(extension);
        }

        foreach ((ElementSourceHandlerExtension extension, List<Registration> registrations)
                 in removedRegistrations)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => DisposeRegistrationsAsync(registrations));
        }

        foreach (ElementSourceHandlerExtension extension in currentExtensions)
        {
            if (_extensionRegistrations.ContainsKey(extension))
                continue;

            var registrations = new List<Registration>();
            try
            {
                foreach (ElementSourceHandlerRegistration registration in ValidateRegistrations(extension))
                {
                    registrations.Add((Registration)Register(registration));
                }

                _extensionRegistrations.Add(extension, registrations);
            }
            catch (Exception ex)
            {
                ExtensionRegistrationLifetimes.Retire(
                    extension,
                    () => DisposeRegistrationsAsync(registrations));

                ReportFailure(extension, ex);
            }
        }
    }

    private Task UnregisterAsync(Registration registration, RegistrationState state)
    {
        Task drain;
        lock (_gate)
        {
            drain = state.RetireAsync();
            if (_handlers.TryGetValue(state.SourceType, out List<Registration>? entries))
            {
                entries.Remove(registration);
                if (entries.Count == 0)
                {
                    _handlers.Remove(state.SourceType);
                }
            }
        }

        return drain;
    }

    private static ValueTask DisposeRegistrationsAsync(IEnumerable<Registration> registrations)
    {
        Task[] drains = registrations
            .Select(registration => registration.DisposeAsync().AsTask())
            .ToArray();
        return drains.Length == 0
            ? ValueTask.CompletedTask
            : new ValueTask(Task.WhenAll(drains));
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
        private TaskCompletionSource? _drained;
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

        public Task RetireAsync()
        {
            lock (_gate)
            {
                _retired = true;
                return _activeLeases == 0
                    ? Task.CompletedTask
                    : (_drained ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        public void ReleaseLease()
        {
            TaskCompletionSource? drained = null;
            lock (_gate)
            {
                _activeLeases--;
                if (_activeLeases == 0 && _retired)
                {
                    drained = _drained;
                }
            }

            drained?.TrySetResult();
        }
    }

    private sealed class Registration : IElementSourceHandlerRegistration
    {
        private readonly Lazy<Task> _retirement;
        private RegistrationOwner? _owner;

        public Registration(ElementSourceHandlerRegistry owner, RegistrationState state)
        {
            _owner = new RegistrationOwner(owner, state);
            _retirement = new Lazy<Task>(
                RetireCoreAsync,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public RegistrationState State => Volatile.Read(ref _owner)?.State
            ?? throw new ObjectDisposedException(nameof(IElementSourceHandlerRegistration));

        public bool TryAcquire([NotNullWhen(true)] out HandlerLease? lease)
        {
            RegistrationOwner? current = Volatile.Read(ref _owner);
            if (_retirement.IsValueCreated || current is null)
            {
                lease = null;
                return false;
            }

            return current.State.TryAcquire(out lease);
        }

        public ValueTask DisposeAsync()
            => new(_retirement.Value);

        private async Task RetireCoreAsync()
        {
            RegistrationOwner? current = Volatile.Read(ref _owner);
            if (current is null)
                return;

            try
            {
                await current.Registry.UnregisterAsync(this, current.State).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _owner, null);
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
