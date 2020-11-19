using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.PropertyData.Bindings
{
    public interface IBindable<T> : IObservable<T>, IObserver<T>, IPropertyElement
    {
        public T Value { get; }

        public void Bind(IBindable<T> bindable);
    }
}
