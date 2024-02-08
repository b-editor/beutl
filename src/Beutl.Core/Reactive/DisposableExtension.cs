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

    public static void DisposeAll<T1, T2, T3, T4>(in this ValueTuple<T1?, T2?, T3?, T4?> tuple)
        where T1 : IDisposable
        where T2 : IDisposable
        where T3 : IDisposable
        where T4 : IDisposable
    {
        tuple.Item1?.Dispose();
        tuple.Item2?.Dispose();
        tuple.Item3?.Dispose();
        tuple.Item4?.Dispose();
    }

    public static void DisposeAll<T1, T2, T3, T4, T5>(in this ValueTuple<T1?, T2?, T3?, T4?, T5?> tuple)
        where T1 : IDisposable
        where T2 : IDisposable
        where T3 : IDisposable
        where T4 : IDisposable
        where T5 : IDisposable
    {
        tuple.Item1?.Dispose();
        tuple.Item2?.Dispose();
        tuple.Item3?.Dispose();
        tuple.Item4?.Dispose();
        tuple.Item5?.Dispose();
    }

    public static void DisposeAll<T1, T2, T3, T4, T5, T6>(in this ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?> tuple)
        where T1 : IDisposable
        where T2 : IDisposable
        where T3 : IDisposable
        where T4 : IDisposable
        where T5 : IDisposable
        where T6 : IDisposable
    {
        tuple.Item1?.Dispose();
        tuple.Item2?.Dispose();
        tuple.Item3?.Dispose();
        tuple.Item4?.Dispose();
        tuple.Item5?.Dispose();
        tuple.Item6?.Dispose();
    }

    public static void DisposeAll<T1, T2, T3, T4, T5, T6, T7>(in this ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?> tuple)
        where T1 : IDisposable
        where T2 : IDisposable
        where T3 : IDisposable
        where T4 : IDisposable
        where T5 : IDisposable
        where T6 : IDisposable
        where T7 : IDisposable
    {
        tuple.Item1?.Dispose();
        tuple.Item2?.Dispose();
        tuple.Item3?.Dispose();
        tuple.Item4?.Dispose();
        tuple.Item5?.Dispose();
        tuple.Item6?.Dispose();
        tuple.Item7?.Dispose();
    }
}
