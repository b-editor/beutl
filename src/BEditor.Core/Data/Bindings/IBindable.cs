using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;

namespace BEditor.Data.Bindings
{
    /// <summary>
    /// Represents a Bindable object.
    /// </summary>
    /// <typeparam name="T">Type of object to bind</typeparam>
    public interface IBindable<T> : IBindable, IObservable<T>, IObserver<T>
    {
        /// <summary>
        /// Get a value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Bind this object to <paramref name="bindable"/>.
        /// </summary>
        /// <param name="bindable"></param>
        public void Bind(IBindable<T>? bindable);
    }
    /// <summary>
    /// Represents a Bindable object.
    /// </summary>
    public interface IBindable : IPropertyElement
    {
        /// <summary>
        /// Gets a hint to use when searching for objects to Bind.
        /// </summary>
        public string? BindHint { get; }
    }
}
