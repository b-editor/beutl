using System.Diagnostics;

namespace Beutl.Media.Source;

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

    public void AddRef()
    {
        if (!TryAddRef())
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

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
