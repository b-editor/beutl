namespace Beutl.Reactive;

public static class DisposableExtension
{
    public static T DisposeWith<T>(this T disposable, ICollection<IDisposable> list)
        where T : IDisposable
    {
        list.Add(disposable);
        return disposable;
    }

    public static void DisposeAll<T1, T2>(in this ValueTuple<T1?, T2?> tuple)
        where T1 : IDisposable
        where T2 : IDisposable
    {
        tuple.Item1?.Dispose();
        tuple.Item2?.Dispose();
    }

    public static void DisposeAll<T1, T2, T3>(in this ValueTuple<T1?, T2?, T3?> tuple)
        where T1 : IDisposable
        where T2 : IDisposable
        where T3 : IDisposable
    {
        tuple.Item1?.Dispose();
        tuple.Item2?.Dispose();
        tuple.Item3?.Dispose();
    }
}
