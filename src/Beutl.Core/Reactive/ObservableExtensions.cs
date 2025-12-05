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
}
