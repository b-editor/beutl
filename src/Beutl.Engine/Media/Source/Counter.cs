using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Beutl.Media.Source;

/// <summary>
/// Internal reference-counted owner for a disposable resource that may be
/// shared across <see cref="MediaSource"/> resources. The instance starts
/// with <c>RefCount = 1</c>; the constructing caller owns that initial
/// reference and must call <see cref="Release"/> exactly once when finished.
/// </summary>
/// <remarks>
/// All public members are thread-safe. <see cref="TryAddRef"/> atomically
/// publishes the "alive" state — once it returns <see langword="true"/>, the
/// caller is guaranteed to read a non-disposed <see cref="Value"/> until the
/// matching <see cref="Release"/>. This contract is relied on by
/// <c>VideoSource</c>, <c>ImageSource</c> and <c>SoundSource</c> to close the
/// TOCTOU window between observing a live counter via
/// <see cref="WeakReference{T}"/> and joining it.
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
    /// Adds a reference. Throws <see cref="ObjectDisposedException"/> if the
    /// counter has already reached zero. Use <see cref="TryAddRef"/> from
    /// shared-cache paths where racing with the final <see cref="Release"/>
    /// is expected.
    /// </summary>
    public void AddRef()
    {
        if (!TryAddRef())
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    /// <summary>
    /// Atomically attempts to add a reference. Returns <see langword="true"/>
    /// only when the counter was still alive at the moment of the call, in
    /// which case the caller now owns one reference and may safely read
    /// <see cref="Value"/> until they call <see cref="Release"/>. Returns
    /// <see langword="false"/> if the counter has already reached zero —
    /// callers fall back to creating a fresh resource in that case.
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
    /// Drops one reference. When the count reaches zero, invokes the
    /// <c>onRelease</c> callback and then disposes the wrapped value
    /// (in that order). Both calls happen outside the internal lock to
    /// avoid re-entrant deadlocks. Calling <see cref="Release"/> after the
    /// counter has already reached zero is a no-op; the surplus call is
    /// logged via <see cref="Trace"/> to surface double-release bugs.
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

        Exception? failure = null;
        try
        {
            onRelease?.Invoke();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            toDispose?.Dispose();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>
    /// Returns the wrapped value. Throws <see cref="ObjectDisposedException"/>
    /// when called after the final <see cref="Release"/>. Holding an
    /// outstanding reference (acquired via the constructor, <see cref="AddRef"/>
    /// or a successful <see cref="TryAddRef"/>) is what makes this access safe.
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
    /// Diagnostic snapshot of the current reference count. The value can
    /// change immediately after the call returns and must not be used to
    /// gate liveness decisions — use <see cref="TryAddRef"/> for that.
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
