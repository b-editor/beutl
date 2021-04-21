using System;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public class EditingProperty<TValue> : EditingProperty, IEditingProperty<TValue>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty{TValue}"/> class.
        /// </summary>
        /// <param name="key">The registry key..</param>
        public EditingProperty(EditingPropertyRegistryKey key) : base(typeof(TValue), key)
        {
        }

        /// <inheritdoc/>
        public new IEditingPropertySerializer<TValue>? Serializer
        {
            get => base.Serializer as IEditingPropertySerializer<TValue>;
            init => base.Serializer = value;
        }

        /// <inheritdoc/>
        public new IEditingPropertyInitializer<TValue>? Initializer
        {
            get => base.Initializer as IEditingPropertyInitializer<TValue>;
            init => base.Initializer = value;
        }
    }
}