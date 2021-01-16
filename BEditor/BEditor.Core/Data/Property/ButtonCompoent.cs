using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;


namespace BEditor.Core.Data.Property
{
    [DataContract]
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
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
            {
                state.observer.OnCompleted();
                state.Item2.Collection.Remove(state.observer);
            });
        }

        public void Execute()
        {
            var tmp = new object();
            foreach (var observer in Collection)
            {
                try
                {
                    observer.OnNext(tmp);
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
        }
    }
}
