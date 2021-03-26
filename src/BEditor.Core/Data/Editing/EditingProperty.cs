using System;
using System.Collections.Generic;

using BEditor.Properties;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    public class EditingProperty
    {
        internal static readonly Dictionary<PropertyKey, EditingProperty> PropertyFromKey = new();


        internal EditingProperty(string name, Type owner, Type value, IPropertyBuilder? builder = null)
        {
            Name = name;
            OwnerType = owner;
            ValueType = value;
            Builder = builder;
        }


        /// <summary>
        /// Gets the name of the property.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the owner type of the property.
        /// </summary>
        public Type OwnerType { get; }

        /// <summary>
        /// Gets the value type of the property.
        /// </summary>
        public Type ValueType { get; }

        /// <summary>
        /// Gets the <see cref="IPropertyBuilder"/> that initializes the local value of a property.
        /// </summary>
        public IPropertyBuilder? Builder { get; }


        /// <summary>
        /// Registers a editor property with the specified property name, value type, and owner type.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="builder">The <see cref="IPropertyBuilder{T}"/> that initializes the local value of a property.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static EditingProperty<TValue> Register<TValue, TOwner>(string name, IPropertyBuilder<TValue>? builder = null) where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner));

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(ExceptionMessage.KeyHasAlreadyBeenRegisterd);
            }
            var property = new EditingProperty<TValue>(name, typeof(TOwner), builder);

            PropertyFromKey.Add(key, property);

            return property;
        }


        internal record PropertyKey(string Name, Type OwnerType);
    }
}
