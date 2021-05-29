// EditingPropertySerializer{TValue}.cs
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
    /// Represents the serializer of <see cref="IEditingProperty"/>.
    /// </summary>
    /// <typeparam name="TValue">The type of the local value.</typeparam>
    public class EditingPropertySerializer<TValue> : IEditingPropertySerializer<TValue>
    {
        private readonly Action<Utf8JsonWriter, TValue> _write;
        private readonly Func<JsonElement, TValue> _read;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditingPropertySerializer{TValue}"/> class.
        /// </summary>
        /// <param name="write">Writes the value to <see cref="Utf8JsonWriter"/>.</param>
        /// <param name="read">Reads the value from <see cref="JsonElement"/>.</param>
        public EditingPropertySerializer(Action<Utf8JsonWriter, TValue> write, Func<JsonElement, TValue> read)
        {
            (_write, _read) = (write, read);
        }

        /// <inheritdoc/>
        public void Write(Utf8JsonWriter writer, TValue value)
        {
            _write(writer, value!);
        }

        /// <inheritdoc/>
        public TValue Read(JsonElement element)
        {
            return _read(element);
        }
    }
}