namespace Beutl.Media.Source;

public sealed class Ref<T> : IDisposable
    where T : class, IDisposable
{
    private readonly Counter<T> _counter;
    private readonly object _lock = new();

    // Todo: Ref<SKSurface>がファイナライザーでDisposeされるとき、AccessViolationExceptionが発生したことがある。
    // なのでどこで作成されたかの情報が欲しい。
#if DEBUG
#pragma warning disable IDE0052 // 読み取られていないプライベート メンバーを削除
    private readonly string _stackTrace;
#pragma warning restore IDE0052 // 読み取られていないプライベート メンバーを削除
#endif

    private Ref(T value, Counter<T> counter)
    {
#if DEBUG
        _stackTrace = Environment.StackTrace;
#endif
        Value = value;
        _counter = counter;
    }

    public static Ref<T> Create(T value, Action? onRelease = null)
    {
        var counter = new Counter<T>(value, onRelease);
        return new Ref<T>(value, counter);
    }

    ~Ref()
    {
        Dispose();
    }

    public T Value { get; private set; }

    public int RefCount => _counter.RefCount;

    public Ref<T> Clone()
    {
        lock (_lock)
        {
            if (Value != null)
            {
                var newRef = new Ref<T>(Value, _counter);
                _counter.AddRef();
                return newRef;
            }
            throw new ObjectDisposedException("Ref<" + typeof(T) + ">");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (Value != null)
            {
                _counter.Release();
                Value = null!;
            }
            GC.SuppressFinalize(this);
        }
    }
}
