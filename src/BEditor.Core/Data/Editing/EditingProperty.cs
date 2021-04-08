using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;

using BEditor.Resources;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    public interface IEditingProperty
    {
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
        /// Gets the <see cref="IEditingPropertyInitializer"/> that initializes the local value of a property.
        /// </summary>
        public IEditingPropertyInitializer? Initializer { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer"/> that serializes the local value of a property.
        /// </summary>
        public IEditingPropertySerializer? Serializer { get; init; }
    }

    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    public abstract class EditingProperty : IEditingProperty
    {
        /// <summary>
        /// 登録された全ての <see cref="EditingProperty"/> です.
        /// </summary>
        internal static readonly Dictionary<PropertyKey, EditingProperty> PropertyFromKey = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty"/> class.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="owner">The type of the owner.</param>
        /// <param name="value">The type of the local value.</param>
        protected EditingProperty(string name, Type owner, Type value)
        {
            Name = name;
            OwnerType = owner;
            ValueType = value;
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public Type OwnerType { get; }

        /// <inheritdoc/>
        public Type ValueType { get; }

        /// <inheritdoc/>
        public IEditingPropertyInitializer? Initializer { get; init; }

        /// <inheritdoc/>
        public IEditingPropertySerializer? Serializer { get; init; }

        /// <summary>
        /// Registers a <see cref="EditingProperty"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="initializer">The <see cref="IEditingPropertyInitializer{T}"/> that initializes the local value of a property.</param>
        /// <param name="serializer">To serialize this property, specify the serializer.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static EditingProperty<TValue> Register<TValue, TOwner>(
            string name,
            IEditingPropertyInitializer<TValue>? initializer = null,
            EditingPropertySerializer<TValue>? serializer = null)
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner));

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(Strings.KeyHasAlreadyBeenRegisterd);
            }

            var property = new EditingProperty<TValue>(name, typeof(TOwner))
            {
                Initializer = initializer,
                Serializer = serializer
            };

            PropertyFromKey.Add(key, property);

            return property;
        }

        /// <summary>
        /// Registers a serializable <see cref="EditingProperty"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="initializer">The <see cref="IEditingPropertyInitializer{T}"/> that initializes the local value of a property.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static EditingProperty<TValue> RegisterSerialize<TValue, TOwner>(
            string name,
            IEditingPropertyInitializer<TValue>? initializer = null)
            where TValue : IJsonObject
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner));

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(Strings.KeyHasAlreadyBeenRegisterd);
            }

            var property = new EditingProperty<TValue>(name, typeof(TOwner))
            {
                Initializer = initializer,
                Serializer = PropertyJsonSerializer<TValue>.Current
            };

            PropertyFromKey.Add(key, property);

            return property;
        }

        /// <summary>
        /// Registers a direct <see cref="EditingProperty"/>
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="getter">Gets the current value of the property.</param>
        /// <param name="setter">Sets the value of the property.</param>
        /// <param name="initializer">The <see cref="IEditingPropertyInitializer{T}"/> that initializes the local value of a property.</param>
        /// <param name="serializer">To serialize this property, specify the serializer.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static DirectEditingProperty<TOwner, TValue> RegisterDirect<TValue, TOwner>(
            string name,
            Func<TOwner, TValue> getter,
            Action<TOwner, TValue> setter,
            IEditingPropertyInitializer<TValue>? initializer = null,
            EditingPropertySerializer<TValue>? serializer = null)
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner));

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(Strings.KeyHasAlreadyBeenRegisterd);
            }

            var property = new DirectEditingProperty<TOwner, TValue>(name, getter, setter)
            {
                Initializer = initializer,
                Serializer = serializer
            };

            PropertyFromKey.Add(key, property);

            return property;
        }

        /// <summary>
        /// <see cref="BEditor.Data.EditingProperty.PropertyFromKey"/> のキーです.
        /// </summary>
        /// <param name="Name">The name of the property.</param>
        /// <param name="OwnerType">The owner type of the property.</param>
        internal record PropertyKey(string Name, Type OwnerType);
    }
}
