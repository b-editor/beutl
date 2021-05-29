// EditingPropertyOptions{TValue}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Text.Json;

namespace BEditor.Data
{
    /// <summary>
    /// Provides options for use with <see cref="EditingProperty.Register{TValue, TOwner}(string, EditingPropertyOptions{TValue})"/>.
    /// </summary>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public struct EditingPropertyOptions<TValue>
    {
        /// <summary>
        /// Gets or sets the <see cref="IEditingPropertyInitializer{T}"/> that initializes the value of a property.
        /// </summary>
        public IEditingPropertyInitializer<TValue>? Initializer { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="IEditingPropertySerializer{T}"/> to serialize this property.
        /// </summary>
        public IEditingPropertySerializer<TValue>? Serializer { get; set; }

        /// <summary>
        /// Gets or sets the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.
        /// </summary>
        public bool IsDisposable { get; set; }

        /// <summary>
        /// Initialize with each option specified.
        /// </summary>
        /// <param name="initializer">The <see cref="IEditingPropertyInitializer{T}"/> that initializes the local value of a property.</param>
        /// <param name="serializer">The <see cref="IEditingPropertySerializer{T}"/> to serialize this property.</param>
        /// <param name="isDisposable">The value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public static EditingPropertyOptions<TValue> Create(
            IEditingPropertyInitializer<TValue>? initializer = null,
            IEditingPropertySerializer<TValue>? serializer = null,
            bool isDisposable = false)
        {
            return new EditingPropertyOptions<TValue>
            {
                Initializer = initializer,
                Serializer = serializer,
                IsDisposable = isDisposable,
            };
        }

        /// <summary>
        /// Sets the <see cref="IEditingPropertyInitializer{T}"/> that initializes the value of a property.
        /// </summary>
        /// <param name="initializer">The value of <see cref="Initializer"/>.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public EditingPropertyOptions<TValue> Initialize(IEditingPropertyInitializer<TValue>? initializer)
        {
            Initializer = initializer;
            return this;
        }

        /// <summary>
        /// Sets the <see cref="IEditingPropertySerializer{T}"/> to serialize this property.
        /// </summary>
        /// <param name="serializer">The value of <see cref="Serializer"/>.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public EditingPropertyOptions<TValue> Serialize(IEditingPropertySerializer<TValue> serializer)
        {
            Serializer = serializer;
            return this;
        }

        /// <summary>
        /// Sets the <see cref="IEditingPropertyInitializer{T}"/> that initializes the value of a property.
        /// </summary>
        /// <param name="initialize">Create a new instance.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public EditingPropertyOptions<TValue> Initialize(Func<TValue> initialize)
        {
            Initializer = new EditingPropertyInitializer<TValue>(initialize);
            return this;
        }

        /// <summary>
        /// Sets the <see cref="IEditingPropertySerializer{T}"/> to serialize this property.
        /// </summary>
        /// <param name="write">Writes the value to <see cref="Utf8JsonWriter"/>.</param>
        /// <param name="read">Reads the value from <see cref="JsonElement"/>.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public EditingPropertyOptions<TValue> Serialize(Action<Utf8JsonWriter, TValue> write, Func<JsonElement, TValue> read)
        {
            Serializer = new EditingPropertySerializer<TValue>(write, read);
            return this;
        }

        /// <summary>
        /// Sets the value of whether to delete with <see cref="EditingObject.ClearDisposable"/>.
        /// </summary>
        /// <param name="value">The value of <see cref="IsDisposable"/>.</param>
        /// <returns>An <see cref="EditingPropertyOptions{TValue}"/> instance.</returns>
        public EditingPropertyOptions<TValue> Disposable(bool value)
        {
            IsDisposable = value;
            return this;
        }
    }
}