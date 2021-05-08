using System;

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
        /// Gets the name of this <see cref="IEditingProperty"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the owner type of this <see cref="IEditingProperty"/>.
        /// </summary>
        public Type OwnerType { get; }

        /// <summary>
        /// Gets the value type of this <see cref="IEditingProperty"/>.
        /// </summary>
        public Type ValueType { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertyInitializer"/> that initializes the local value of this <see cref="IEditingProperty"/>.
        /// </summary>
        public IEditingPropertyInitializer? Initializer { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer"/> that serializes the local value of this <see cref="IEditingProperty"/>.
        /// </summary>
        public IEditingPropertySerializer? Serializer { get; init; }

        /// <summary>
        /// Gets the registry key.
        /// </summary>
        public EditingPropertyRegistryKey Key { get; }
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

        /// <summary>
        /// Gets the <see cref="IEditingPropertyInitializer{TValue}"/> that initializes the local value of this <see cref="IEditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertyInitializer<TValue>? Initializer { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer{TValue}"/> that serializes the local value of this <see cref="IEditingProperty{TValue}"/>.
        /// </summary>
        public new IEditingPropertySerializer<TValue>? Serializer { get; init; }
    }

    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    public abstract class EditingProperty : IEditingProperty
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty"/> class.
        /// </summary>
        /// <param name="value">The type of the local value.</param>
        /// <param name="key">The registry key</param>
        protected EditingProperty(Type value, EditingPropertyRegistryKey key)
        {
            ValueType = value;
            Key = key;
        }

        /// <inheritdoc/>
        public bool IsDisposable => Key.IsDisposable;

        /// <summary>
        /// Gets the name of this <see cref="EditingProperty"/>.
        /// </summary>
        public string Name => Key.Name;

        /// <summary>
        /// Gets the owner type of this <see cref="EditingProperty"/>.
        /// </summary>
        public Type OwnerType => Key.OwnerType;

        /// <summary>
        /// Gets the value type of this <see cref="EditingProperty"/>.
        /// </summary>
        public Type ValueType { get; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertyInitializer"/> that initializes the local value of this <see cref="EditingProperty"/>.
        /// </summary>
        public IEditingPropertyInitializer? Initializer { get; init; }

        /// <summary>
        /// Gets the <see cref="IEditingPropertySerializer"/> that serializes the local value of this <see cref="EditingProperty"/>.
        /// </summary>
        public IEditingPropertySerializer? Serializer { get; init; }

        /// <inheritdoc/>
        public EditingPropertyRegistryKey Key { get; }

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
            var key = new EditingPropertyRegistryKey(name, typeof(TOwner), isDisposable);

            if (EditingPropertyRegistry.IsRegistered(key))
            {
                throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
            }

            var property = new EditingProperty<TValue>(key)
            {
                Initializer = initializer,
                Serializer = serializer,
            };

            EditingPropertyRegistry.RegisterUnChecked(key, property);

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
            var key = new EditingPropertyRegistryKey(name, typeof(TOwner), isDisposable);

            if (EditingPropertyRegistry.IsRegistered(key))
            {
                throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
            }

            var property = new EditingProperty<TValue>(key)
            {
                Initializer = initializer,
                Serializer = PropertyJsonSerializer<TValue>.Current,
            };

            EditingPropertyRegistry.RegisterUnChecked(key, property);

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
            var key = new EditingPropertyRegistryKey(name, typeof(TOwner), false);

            if (EditingPropertyRegistry.IsRegistered(key))
            {
                throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
            }

            var property = new DirectEditingProperty<TOwner, TValue>(getter, setter, key)
            {
                Initializer = initializer,
                Serializer = serializer,
            };

            EditingPropertyRegistry.RegisterUnChecked(key, property);

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
            var key = new EditingPropertyRegistryKey(name, typeof(TOwner), false);

            if (EditingPropertyRegistry.IsRegistered(key))
            {
                throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
            }

            var property = new DirectEditingProperty<TOwner, TValue>(getter, setter, key)
            {
                Initializer = initializer,
                Serializer = PropertyJsonSerializer<TValue>.Current,
            };

            EditingPropertyRegistry.RegisterUnChecked(key, property);

            return property;
        }
    }
}