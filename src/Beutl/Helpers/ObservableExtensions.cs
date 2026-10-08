namespace Beutl;

public static class ObservableExtensions
{
    public static IObservable<bool> AreTrue(
        this IObservable<bool> first,
        IObservable<bool> second)
    {
        return first.CombineLatest(second)
            .Select(x => x.First && x.Second);
    }

    public static IObservable<bool> AreTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third)
    {
        return first.CombineLatest(second, third)
            .Select(x => x.First && x.Second && x.Third);
    }

    public static IObservable<bool> AreTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth)
    {
        return first.CombineLatest(second, third, fourth)
            .Select(x => x.First && x.Second && x.Third && x.Fourth);
    }

    public static IObservable<bool> AnyTrue(
        this IObservable<bool> first,
        IObservable<bool> second)
    {
        return first.CombineLatest(second)
            .Select(x => x.First || x.Second);
    }

    public static IObservable<bool> Not(this IObservable<bool> source)
    {
        return source.Select(x => !x);
    }

    public static IObservable<(TSource? OldValue, TSource? NewValue)> CombineWithPrevious<TSource>(this IObservable<TSource> source)
    {
        return source.Scan((default(TSource), default(TSource)), (previous, current) => (previous.Item2, current))
            .Select(t => (t.Item1, t.Item2));
    }
}
