using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Bindings
{
    public interface IBindable<T> : IObservable<T>, IObserver<T>, IPropertyElement
    {
        public T Value { get; }
        public string BindHint { get; }

        public void Bind(IBindable<T> bindable);
    }
}
