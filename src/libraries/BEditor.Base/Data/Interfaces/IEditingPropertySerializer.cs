// IEditingPropertySerializer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Text.Json;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the serializer of <see cref="IEditingProperty"/>.
    /// </summary>
    public interface IEditingPropertySerializer
    {
        /// <summary>
        /// Writes the value to <see cref="Utf8JsonWriter"/>.
        /// </summary>
        /// <param name="writer">The <see cref="Utf8JsonWriter"/> that writes the value.</param>
        /// <param name="value">The value to be written.</param>
        public void Write(Utf8JsonWriter writer, object value);

        /// <summary>
        /// Reads the value from <see cref="JsonElement"/>.
        /// </summary>
        /// <param name="element">The <see cref="JsonElement"/> that reads the value.</param>
        /// <returns>Returns the data of an object recovered from Json.</returns>
        public object Read(JsonElement element);
    }
}