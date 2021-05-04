using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;

namespace BEditor.Data.Bindings
{
    /// <summary>
    /// Represents the Bindable object.
    /// </summary>
    /// <typeparam name="T">Type of object to bind.</typeparam>
    public interface IBindable<T> : IPropertyElement, IObservable<T>, IObserver<T>
    {
        /// <summary>
        /// Gets the value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Gets a hint to use when searching for objects to Bind.
        /// </summary>
        public Guid? TargetID { get; }

        /// <summary>
        /// Bind this object to <paramref name="bindable"/>.
        /// </summary>
        /// <param name="bindable">The object to bind.</param>
        public void Bind(IBindable<T>? bindable);
    }
}