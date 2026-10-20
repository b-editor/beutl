using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services;

public sealed partial class ElementSourceHandlerRegistry
{
    private sealed class RegistrationState(
        Type sourceType,
        IElementSourceHandler handler)
    {
        private readonly LeaseDrain _drain = new();

        public Type SourceType { get; } = sourceType;

        public IElementSourceHandler Handler { get; } = handler;

        public bool TryAcquire([NotNullWhen(true)] out HandlerLease? lease)
        {
            lease = _drain.TryAcquire() ? new HandlerLease(this) : null;
            return lease is not null;
        }

        public Task RetireAsync() => _drain.RetireAsync();

        public void ReleaseLease() => _drain.Release();
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
