﻿namespace Beutl;

public static class ObservableExtensions
{
    public static IObservable<bool> EqualTo<T>(
        this IObservable<T> left,
        IObservable<T> right)
    {
        return left.CombineLatest(right).Select(t => EqualityComparer<T>.Default.Equals(t.First, t.Second));
    }

    public static IObservable<bool> EqualTo<T>(
        this IObservable<T> left,
        IObservable<T> right,
        Func<T, T, bool> equalityComparer)
    {
        return left.CombineLatest(right).Select(t => equalityComparer.Invoke(t.First, t.Second));
    }

    public static IObservable<bool> EqualTo<T>(
        this IObservable<T> left,
        IObservable<T> right,
        IEqualityComparer<T> equalityComparer)
    {
        return left.CombineLatest(right).Select(t => equalityComparer.Equals(t.First, t.Second));
    }

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

    public static IObservable<bool> AreTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth,
        IObservable<bool> fifth)
    {
        return first.CombineLatest(second, third, fourth, fifth)
            .Select(x => x.First && x.Second && x.Third && x.Fourth && x.Fifth);
    }

    public static IObservable<bool> AreTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth,
        IObservable<bool> fifth,
        IObservable<bool> sixth)
    {
        return first.CombineLatest(second, third, fourth, fifth, sixth)
            .Select(x => x.First && x.Second && x.Third && x.Fourth && x.Fifth && x.Sixth);
    }

    public static IObservable<bool> AreTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth,
        IObservable<bool> fifth,
        IObservable<bool> sixth,
        IObservable<bool> seventh,
        IObservable<bool> eighth)
    {
        return first.CombineLatest(second, third, fourth, fifth, sixth, seventh, eighth)
            .Select(x => x.First && x.Second && x.Third && x.Fourth && x.Fifth && x.Sixth && x.Seventh && x.Eighth);
    }

    public static IObservable<bool> AnyTrue(
        this IObservable<bool> first,
        IObservable<bool> second)
    {
        return first.CombineLatest(second)
            .Select(x => x.First || x.Second);
    }

    public static IObservable<bool> AnyTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third)
    {
        return first.CombineLatest(second, third)
            .Select(x => x.First || x.Second || x.Third);
    }

    public static IObservable<bool> AnyTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth)
    {
        return first.CombineLatest(second, third, fourth)
            .Select(x => x.First || x.Second || x.Third || x.Fourth);
    }

    public static IObservable<bool> AnyTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth,
        IObservable<bool> fifth)
    {
        return first.CombineLatest(second, third, fourth, fifth)
            .Select(x => x.First || x.Second || x.Third || x.Fourth || x.Fifth);
    }

    public static IObservable<bool> AnyTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth,
        IObservable<bool> fifth,
        IObservable<bool> sixth)
    {
        return first.CombineLatest(second, third, fourth, fifth, sixth)
            .Select(x => x.First || x.Second || x.Third || x.Fourth || x.Fifth || x.Sixth);
    }

    public static IObservable<bool> AnyTrue(
        this IObservable<bool> first,
        IObservable<bool> second,
        IObservable<bool> third,
        IObservable<bool> fourth,
        IObservable<bool> fifth,
        IObservable<bool> sixth,
        IObservable<bool> seventh,
        IObservable<bool> eighth)
    {
        return first.CombineLatest(second, third, fourth, fifth, sixth, seventh, eighth)
            .Select(x => x.First || x.Second || x.Third || x.Fourth || x.Fifth || x.Sixth || x.Seventh || x.Eighth);
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

    public static IObservable<TResult> CombineWithPrevious<TSource, TResult>(
        this IObservable<TSource> source,
        Func<TSource?, TSource?, TResult> resultSelector)
    {
        return source.Scan((default(TSource), default(TSource)), (previous, current) => (previous.Item2, current))
            .Select(t => resultSelector(t.Item1, t.Item2));
    }
}
