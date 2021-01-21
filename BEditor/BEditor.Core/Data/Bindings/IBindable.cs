using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Bindings
{
    public interface IBindable<T> : IBindable, IObservable<T>, IObserver<T>
    {
        public T Value { get; }

        public void Bind(IBindable<T>? bindable);
    }

    public interface IBindable : IPropertyElement
    {
        public string? BindHint { get; }
    }
}
