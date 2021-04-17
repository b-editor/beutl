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
        /// Get the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.
        /// </summary>
        public bool IsDisposable { get; }

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
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public interface IEditingProperty<TValue> : IEditingProperty
    {
        IEditingPropertyInitializer? IEditingProperty.Initializer => Initializer;

        IEditingPropertySerializer? IEditingProperty.Serializer
        {
            get => Serializer;
            init => Serializer = (IEditingPropertySerializer<TValue>?)value;
        }

        /// <inheritdoc cref="IEditingProperty.Initializer"/>
        public new IEditingPropertyInitializer<TValue>? Initializer { get; }

        /// <inheritdoc cref="IEditingProperty.Serializer"/>
        public new IEditingPropertySerializer<TValue>? Serializer { get; init; }
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
        /// <param name="isDisposable">the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.</param>
        protected EditingProperty(string name, Type owner, Type value, bool isDisposable)
        {
            Name = name;
            OwnerType = owner;
            ValueType = value;
            IsDisposable = isDisposable;
        }

        /// <inheritdoc/>
        public bool IsDisposable { get; }

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
#nullable disable
        internal PropertyKey Key { get; set; }
#nullable enable

        /// <summary>
        /// Registers a <see cref="EditingProperty"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="initializer">The <see cref="IEditingPropertyInitializer{T}"/> that initializes the local value of a property.</param>
        /// <param name="serializer">To serialize this property, specify the serializer.</param>
        /// <param name="isDisposable">the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static EditingProperty<TValue> Register<TValue, TOwner>(
            string name,
            IEditingPropertyInitializer<TValue>? initializer = null,
            IEditingPropertySerializer<TValue>? serializer = null,
            bool isDisposable = false)
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner), isDisposable);

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(Strings.KeyHasAlreadyBeenRegisterd);
            }

            var property = new EditingProperty<TValue>(name, typeof(TOwner), isDisposable)
            {
                Initializer = initializer,
                Serializer = serializer,
                Key = key
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
        /// <param name="isDisposable">the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static EditingProperty<TValue> RegisterSerialize<TValue, TOwner>(
            string name,
            IEditingPropertyInitializer<TValue>? initializer = null,
            bool isDisposable = false)
            where TValue : IJsonObject
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner), isDisposable);

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(Strings.KeyHasAlreadyBeenRegisterd);
            }

            var property = new EditingProperty<TValue>(name, typeof(TOwner), isDisposable)
            {
                Initializer = initializer,
                Serializer = PropertyJsonSerializer<TValue>.Current,
                Key = key
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
            IEditingPropertySerializer<TValue>? serializer = null)
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner), false);

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(Strings.KeyHasAlreadyBeenRegisterd);
            }

            var property = new DirectEditingProperty<TOwner, TValue>(name, getter, setter)
            {
                Initializer = initializer,
                Serializer = serializer,
                Key = key
            };

            PropertyFromKey.Add(key, property);

            return property;
        }

        /// <summary>
        /// Registers a serializable direct <see cref="EditingProperty"/>
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="getter">Gets the current value of the property.</param>
        /// <param name="setter">Sets the value of the property.</param>
        /// <param name="initializer">The <see cref="IEditingPropertyInitializer{T}"/> that initializes the local value of a property.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static DirectEditingProperty<TOwner, TValue> RegisterSerializeDirect<TValue, TOwner>(
            string name,
            Func<TOwner, TValue> getter,
            Action<TOwner, TValue> setter,
            IEditingPropertyInitializer<TValue>? initializer = null)
            where TValue : IJsonObject
            where TOwner : IEditingObject
        {
            var key = new PropertyKey(name, typeof(TOwner), false);

            if (PropertyFromKey.ContainsKey(key))
            {
                throw new DataException(Strings.KeyHasAlreadyBeenRegisterd);
            }

            var property = new DirectEditingProperty<TOwner, TValue>(name, getter, setter)
            {
                Initializer = initializer,
                Serializer = PropertyJsonSerializer<TValue>.Current,
                Key = key
            };

            PropertyFromKey.Add(key, property);

            return property;
        }

        /// <summary>
        /// <see cref="BEditor.Data.EditingProperty.PropertyFromKey"/> のキーです.
        /// </summary>
        /// <param name="Name">The name of the property.</param>
        /// <param name="OwnerType">The owner type of the property.</param>
        /// <param name="IsDisposable">the value of whether to delete with <see cref="BEditor.Data.EditingObject.ClearDisposable"/>.</param>
        internal record PropertyKey(string Name, Type OwnerType, bool IsDisposable);
    }
}