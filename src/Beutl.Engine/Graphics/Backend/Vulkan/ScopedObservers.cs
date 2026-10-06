namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Observers of one diagnostic event, each installed for the async flow that called <see cref="Observe"/> until
/// the returned scope is disposed.
/// </summary>
internal sealed class ScopedObservers<T>
{
    private readonly AsyncLocal<Scope?> _current = new();

    public IDisposable Observe(Action<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new Scope(this, observer, _current.Value);
        _current.Value = scope;
        return scope;
    }

    public void Record(T value)
    {
        for (Scope? scope = _current.Value; scope is not null; scope = scope.Parent)
        {
            try
            {
                scope.Observer(value);
            }
            catch
            {
                // Diagnostics must never affect texture allocation, rendering or cleanup.
            }
        }
    }

    private sealed class Scope(
        ScopedObservers<T> owner,
        Action<T> observer,
        Scope? parent) : IDisposable
    {
        private bool _disposed;

        public Action<T> Observer { get; } = observer;

        public Scope? Parent { get; } = parent;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(owner._current.Value, this))
            {
                owner._current.Value = Parent;
            }
        }
    }
}
