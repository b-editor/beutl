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
        private static int _nextId;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditingProperty"/> class.
        /// </summary>
        /// <param name="name">The name of the property.</param>
        /// <param name="value">The type of the local value.</param>
        /// <param name="owner">The owner type.</param>
        protected EditingProperty(string name, Type value, Type owner)
        {
            Names = name.Split(',');
            Name = Names[0];
            ValueType = value;
            OwnerType = owner;
            Id = _nextId++;
        }

        /// <inheritdoc/>
        public bool IsDisposable { get; init; }

        /// <inheritdoc/>
        public bool NotifyPropertyChanged { get; init; }

        /// <summary>
        /// Gets the name of this <see cref="EditingProperty"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the names of this <see cref="EditingProperty"/>.
        /// </summary>
        public string[] Names { get; }

        /// <summary>
        /// Gets the owner type of this <see cref="EditingProperty"/>.
        /// </summary>
        public Type OwnerType { get; }

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
        public int Id { get; }

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
            var property = new EditingProperty<TValue>(name, typeof(TOwner))
            {
                Initializer = options.Initializer,
                Serializer = options.Serializer,
                IsDisposable = options.IsDisposable,
            };

            EditingPropertyRegistry.Register(typeof(TOwner), property);

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
        public static DirectProperty<TOwner, TValue> RegisterDirect<TValue, TOwner>(
            string name,
            Func<TOwner, TValue> getter,
            Action<TOwner, TValue> setter,
            EditingPropertyOptions<TValue> options = default)
            where TOwner : IEditingObject
        {
            var property = new DirectProperty<TOwner, TValue>(name, getter, setter)
            {
                Initializer = options.Initializer,
                Serializer = options.Serializer,
                IsDisposable = options.IsDisposable,
            };

            EditingPropertyRegistry.Register(typeof(TOwner), property);

            return property;
        }

        /// <summary>
        /// Registers a attached <see cref="EditingProperty"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the property.</typeparam>
        /// <typeparam name="TOwner">The type of the owner.</typeparam>
        /// <param name="name">The name of the property.</param>
        /// <param name="options">The property options.</param>
        /// <returns>Returns the registered <see cref="EditingProperty{TValue}"/>.</returns>
        public static AttachedProperty<TValue> RegisterAttached<TValue, TOwner>(
            string name,
            EditingPropertyOptions<TValue> options = default)
            where TOwner : IEditingObject
        {
            var property = new AttachedProperty<TValue>(name, typeof(TOwner))
            {
                Initializer = options.Initializer,
                Serializer = options.Serializer,
                IsDisposable = options.IsDisposable,
                NotifyPropertyChanged = options.NotifyPropertyChanged,
            };

            EditingPropertyRegistry.RegisterAttached(typeof(TOwner), property);

            return property;
        }
    }
}