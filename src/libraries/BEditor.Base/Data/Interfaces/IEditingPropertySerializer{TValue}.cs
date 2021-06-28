// IEditingPropertySerializer{TValue}.cs
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
    /// <typeparam name="TValue">The type of the local value.</typeparam>
    public interface IEditingPropertySerializer<TValue> : IEditingPropertySerializer
    {
        /// <inheritdoc cref="IEditingPropertySerializer.Write(Utf8JsonWriter, object)"/>
        public void Write(Utf8JsonWriter writer, TValue value);

        /// <inheritdoc cref="IEditingPropertySerializer.Read(JsonElement)"/>
        public new TValue Read(JsonElement element);

        /// <inheritdoc/>
        void IEditingPropertySerializer.Write(Utf8JsonWriter writer, object value)
        {
            if (value is null)
            {
                Write(writer, default!);
            }
            else
            {
                Write(writer, (TValue)value);
            }
        }

        /// <inheritdoc/>
        object IEditingPropertySerializer.Read(JsonElement element)
        {
            return Read(element)!;
        }
    }
}