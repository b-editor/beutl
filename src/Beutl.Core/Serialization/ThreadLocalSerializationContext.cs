using System.Reactive.Disposables;

namespace Beutl.Serialization;

public static class ThreadLocalSerializationContext
{
    [field: ThreadStatic]
    public static ICoreSerializationContext? Current { get; private set; }

    public static IDisposable Enter(ICoreSerializationContext context)
    {
        IDisposable disposable = Disposable.Create(Current, c => Current = c);
        Current = context;

        return disposable;
    }
}
