using System.Diagnostics.CodeAnalysis;

namespace Beutl.Editor.Services;

public sealed class ElementSourceHandlerRegistry : IElementSourceHandlerRegistry
{
    private readonly Dictionary<Type, List<Registration>> _handlers = [];
    private readonly object _gate = new();
    private long _registrationSequence;

    public IReadOnlyList<IElementSourceHandler> Handlers
    {
        get
        {
            lock (_gate)
            {
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

    private void UnregisterAndDrain(Registration registration, RegistrationState state)
    {
        lock (_gate)
        {
            state.Retire();
            if (_handlers.TryGetValue(state.SourceType, out List<Registration>? entries)
                && entries is not null)
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
