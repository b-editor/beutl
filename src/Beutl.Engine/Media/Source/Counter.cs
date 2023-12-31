namespace Beutl.Media.Source;

internal sealed class Counter<T>
    where T : class, IDisposable
{
    private T? _value;
    private Action? _onRelease;
    private volatile int _refs;

    // Todo: Ref<SKSurface>がファイナライザーでDisposeされるとき、AccessViolationExceptionが発生したことがある。
    // なのでどこで作成されたかの情報が欲しい。
    // 可能性として考えられるのは、ImmediateCanvasにそのままのSKSurfaceを渡している点
#if DEBUG
#pragma warning disable IDE0052 // 読み取られていないプライベート メンバーを削除
    private readonly string _stackTrace;
#pragma warning restore IDE0052 // 読み取られていないプライベート メンバーを削除
#endif

    public Counter(T value, Action? onRelease)
    {
#if DEBUG
        _stackTrace = Environment.StackTrace;
#endif
        _value = value;
        _onRelease = onRelease;
        _refs = 1;
    }

    public void AddRef()
    {
        int old = _refs;
        while (true)
        {
            ObjectDisposedException.ThrowIf(old == 0, this);
            //if (old == 0)
            //{
            //    throw new ObjectDisposedException("Cannot add a reference to a nonreferenced item");
            //}
            int current = Interlocked.CompareExchange(ref _refs, old + 1, old);
            if (current == old)
            {
                break;
            }
            old = current;
        }
    }

    public void Release()
    {
        int old = _refs;
        while (true)
        {
            int current = Interlocked.CompareExchange(ref _refs, old - 1, old);

            if (current == old)
            {
                if (old == 1)
                {
                    _onRelease?.Invoke();
                    _onRelease = null;

                    _value?.Dispose();
                    _value = null;
                }
                break;
            }
            old = current;
        }
    }

    public int RefCount => _refs;
}
