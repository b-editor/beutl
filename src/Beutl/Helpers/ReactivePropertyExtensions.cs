using Reactive.Bindings;

namespace Beutl;

public static class ReactivePropertyExtensions
{
    public static ReactiveProperty<T> CopyToReactiveProperty<T>(
        this IReadOnlyReactiveProperty<T> source,
        ReactivePropertyMode mode = ReactivePropertyMode.DistinctUntilChanged | ReactivePropertyMode.RaiseLatestValueOnSubscribe,
        IEqualityComparer<T>? equalityComparer = null)
    {
        return source.ToReactiveProperty(source.Value, mode, equalityComparer);
    }

    public static ReadOnlyReactivePropertySlim<T> CopyToReadOnlyReactivePropertySlim<T>(
        this IReadOnlyReactiveProperty<T> source,
        ReactivePropertyMode mode = ReactivePropertyMode.DistinctUntilChanged | ReactivePropertyMode.RaiseLatestValueOnSubscribe,
        IEqualityComparer<T>? equalityComparer = null)
    {
        return source.ToReadOnlyReactivePropertySlim(source.Value, mode, equalityComparer);
    }

    public static ReadOnlyReactiveProperty<T> CopyToReadOnlyReactiveProperty<T>(
        this IReadOnlyReactiveProperty<T> source,
        ReactivePropertyMode mode = ReactivePropertyMode.DistinctUntilChanged | ReactivePropertyMode.RaiseLatestValueOnSubscribe,
        IEqualityComparer<T>? equalityComparer = null)
    {
        return source.ToReadOnlyReactiveProperty(source.Value, mode, equalityComparer: equalityComparer);
    }
}
