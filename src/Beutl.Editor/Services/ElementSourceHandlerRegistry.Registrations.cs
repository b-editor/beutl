using System.Diagnostics.CodeAnalysis;

namespace Beutl.Editor.Services;

public sealed partial class ElementSourceHandlerRegistry
{
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

        public ElementSourceHandlerDescriptor Descriptor { get; } = new(
            sourceType.AssemblyQualifiedName
                ?? sourceType.FullName
                ?? sourceType.Name,
            order);

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
