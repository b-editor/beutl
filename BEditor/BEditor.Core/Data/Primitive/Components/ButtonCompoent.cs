using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Primitive.Components
{
    public class ButtonComponent : ComponentElement<PropertyElementMetadata>, IObservable<object>
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
        public void Execute()
        {
            foreach (var observer in Collection)
            {
                try
                {
                    observer.OnNext(null);
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
        }
    }
}
