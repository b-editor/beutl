using System.Diagnostics;

namespace Beutl.Media.Source;

/// <summary>
/// A reference-counted owner of a disposable resource shared across <see cref="MediaSource"/> resources. The
/// constructing caller owns the initial reference and releases it once.
/// </summary>
/// <remarks>
/// All public members are thread-safe. Once <see cref="TryAddRef"/> returns <see langword="true"/>, <see cref="Value"/>
/// stays undisposed until the matching <see cref="Release"/>, which closes the race between finding a live counter
/// through a <see cref="WeakReference{T}"/> and joining it.
/// </remarks>
internal sealed class Counter<T>
    where T : class, IDisposable
{
    private readonly Lock _lock = new();
    private T? _value;
    private Action? _onRelease;
    private int _refs;

    public Counter(T value, Action? onRelease)
    {
        _value = value;
        _onRelease = onRelease;
        _refs = 1;
    }

    /// <summary>
    /// Adds a reference, throwing <see cref="ObjectDisposedException"/> after the count reached zero.
    /// </summary>
    public void AddRef()
    {
        if (!TryAddRef())
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    /// <summary>
    /// Atomically adds a reference while the counter is alive; <see langword="false"/> once it reached zero.
    /// </summary>
    public bool TryAddRef()
    {
        lock (_lock)
        {
            if (_refs == 0)
            {
                return false;
            }

            _refs++;
            return true;
        }
    }

    /// <summary>
    /// Drops one reference. At zero it invokes <c>onRelease</c> and then disposes the value, both outside the lock to
    /// avoid re-entrant deadlocks. A call past zero is a no-op traced as a possible double release.
    /// </summary>
    public void Release()
    {
        T? toDispose = null;
        Action? onRelease = null;

        lock (_lock)
        {
            if (_refs == 0)
            {
                Trace.WriteLine($"Counter<{typeof(T).FullName}>.Release called past zero - possible double-release bug.{Environment.NewLine}{Environment.StackTrace}");
                return;
            }

            _refs--;
            if (_refs == 0)
            {
                toDispose = _value;
                _value = null;
                onRelease = _onRelease;
                _onRelease = null;
            }
        }

        onRelease?.Invoke();
        toDispose?.Dispose();
    }

    /// <summary>
    /// The wrapped value, safe to read while holding a reference; throws after the final <see cref="Release"/>.
    /// </summary>
    public T Value
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_refs == 0, this);
                return _value!;
            }
        }
    }

    /// <summary>
    /// A diagnostic snapshot that can change right away; use <see cref="TryAddRef"/>, not this, to decide liveness.
    /// </summary>
    public int RefCount
    {
        get
        {
            lock (_lock)
            {
                return _refs;
            }
        }
    }
}
