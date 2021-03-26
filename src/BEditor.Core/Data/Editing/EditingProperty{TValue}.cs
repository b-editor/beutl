using System;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    /// <typeparam name="TValue">The type of the local value.</typeparam>
    public class EditingProperty<TValue> : EditingProperty
    {
        internal EditingProperty(string name, Type owner, IPropertyBuilder<TValue>? builder = null) : base(name, owner, typeof(TValue), builder)
        {

        }

        /// <summary>
        /// Gets the <see cref="IPropertyBuilder{T}"/> that initializes the local value of a property.
        /// </summary>
        public new IPropertyBuilder<TValue>? Builder => base.Builder as IPropertyBuilder<TValue>;
    }
}
