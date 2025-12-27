using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Beutl.Reactive;

public static class ObservableExtensions
{
    extension(Observable)
    {
        public static IObservable<T> ReturnThenNever<T>(T value)
        {
            return Observable.Create<T>(observer =>
            {
                observer.OnNext(value);
                return Disposable.Empty;
            });
        }
    }

    extension<TSource>(IObservable<TSource> source)
    {
        public IObservable<(TSource? OldValue, TSource? NewValue)> CombineWithPrevious()
        {
            return source.Scan((default(TSource), default(TSource)), (previous, current) => (previous.Item2, current))
                .Select(t => (t.Item1, t.Item2));
        }

        public IObservable<TResult> CombineWithPrevious<TResult>(Func<TSource?, TSource?, TResult> resultSelector)
        {
            return source.Scan((default(TSource), default(TSource)), (previous, current) => (previous.Item2, current))
                .Select(t => resultSelector(t.Item1, t.Item2));
        }
    }
}
