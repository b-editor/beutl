using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data.Bindings
{
    internal static class BindingHelper
    {
        public static void AutoLoad<T>(this IBindable<T> bindable, ref string? hint)
        {
            if (hint is not null && bindable.GetBindable(hint, out var b))
            {
                bindable.Bind(b);
            }
            hint = null;
        }

        public static IDisposable Subscribe<T>(IList<IObserver<T>> list, IObserver<T> observer, T value)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            list.Add(observer);

            try
            {
                observer.OnNext(value);
            }
            catch (Exception e)
            {
                observer.OnError(e);
            }

            return Disposable.Create((observer, list), o =>
            {
                o.observer.OnCompleted();
                o.list.Remove(o.observer);
            });
        }

        public static T Bind<T>(this IBindable<T> bindable1, IBindable<T>? bindable2, out IBindable<T>? outbindable, ref IDisposable? disposable)
        {
            disposable?.Dispose();
            outbindable = bindable2;

            if (bindable2 is not null)
            {
                var value = bindable2.Value;

                // bindableが変更時にthisが変更
                disposable = bindable2.Subscribe(bindable1);

                return value;
            }

            return bindable1.Value;
        }
    }
}
