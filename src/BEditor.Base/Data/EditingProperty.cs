// EditingProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Data.Internals;
using BEditor.Resources;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the properties of the edited data.
    /// </summary>
    public abstract class EditingProperty : IEditingProperty
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty"/> class.
        /// </summary>
        /// <param name="value">The type of the local value.</param>
        /// <param name="key">The registry key.</param>
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
        /// <typeparam name="TValue">The type of the property.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="options">The property options.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static EditingProperty<TValue> Register<TValue, TOwner>(string name, EditingPropertyOptions<TValue> options = default)
            where TOwner : IEditingObject
        {
            var key = new EditingPropertyRegistryKey(name, typeof(TOwner), options.IsDisposable);

            if (EditingPropertyRegistry.IsRegistered(key))
            {
                throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
            }

            var property = new EditingProperty<TValue>(key)
            {
                Initializer = options.Initializer,
                Serializer = options.Serializer,
            };

            EditingPropertyRegistry.RegisterUnChecked(key, property);

            return property;
        }

        /// <summary>
        /// Registers a direct <see cref="EditingProperty"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the property.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="getter">Gets the current value of the property.</param>
        /// <param name="setter">Sets the value of the property.</param>
        /// <param name="options">The property options.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static DirectEditingProperty<TOwner, TValue> RegisterDirect<TValue, TOwner>(
            string name,
            Func<TOwner, TValue> getter,
            Action<TOwner, TValue> setter,
            EditingPropertyOptions<TValue> options = default)
            where TOwner : IEditingObject
        {
            var key = new EditingPropertyRegistryKey(name, typeof(TOwner), false);

            if (EditingPropertyRegistry.IsRegistered(key))
            {
                throw new DataException($"{Strings.KeyHasAlreadyBeenRegisterd}:{key.Name}");
            }

            var property = new DirectEditingProperty<TOwner, TValue>(getter, setter, key)
            {
                Initializer = options.Initializer,
                Serializer = options.Serializer,
            };

            EditingPropertyRegistry.RegisterUnChecked(key, property);

            return property;
        }
    }
}