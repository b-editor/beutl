using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;

namespace BEditor.Core.Data.Primitive.Components
{
    public class ButtonComponent : ComponentElement<PropertyElementMetadata>, IEasingProperty, IObservable<object>
    {
        private List<IObserver<object>> list;

        public ButtonComponent(PropertyElementMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        private List<IObserver<object>> Collection => list ??= new();

        public IDisposable Subscribe(IObserver<object> observer)
        {
            Collection.Add(observer);
            return Disposable.Create(() => Collection.Remove(observer));
        }
        public IDisposable Subscribe(Action onNext)
        {
            var observer = new Observer()
            {
                _OnNext = _ => onNext?.Invoke()
            };
            Collection.Add(observer);
            return Disposable.Create(() => Collection.Remove(observer));
        }

        public void Execute()
        {
            // Rxの拡張メソッド対策
            var tmp = new object();
            foreach (var observer in Collection)
            {
                try
                {
                    observer.OnNext(tmp);
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
        }

        private class Observer : IObserver<object>
        {
            public Action<object> _OnNext;
            public Action<Exception> _OnError;
            public Action _OnCompleted;

            public void OnCompleted() => _OnCompleted?.Invoke();
            public void OnError(Exception error) => _OnError?.Invoke(error);
            public void OnNext(object value) => _OnNext?.Invoke(value);
        }
    }
}
