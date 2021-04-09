using System;
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
        public object Read(JsonElement element);
    }

    /// <summary>
    /// Represents the serializer of <see cref="IEditingProperty"/>.
    /// </summary>
    public interface IEditingPropertySerializer<TValue> : IEditingPropertySerializer
    {
        void IEditingPropertySerializer.Write(Utf8JsonWriter writer, object value)
        {
            Write(writer, (TValue)value);
        }
        object IEditingPropertySerializer.Read(JsonElement element)
        {
            return Read(element)!;
        }

        /// <inheritdoc cref="IEditingPropertySerializer.Write(Utf8JsonWriter, object)"/>
        public void Write(Utf8JsonWriter writer, TValue value);

        /// <inheritdoc cref="IEditingPropertySerializer.Read(JsonElement)"/>
        public new TValue Read(JsonElement element);
    }

    /// <summary>
    /// Represents the serializer of <see cref="IEditingProperty"/>.
    /// </summary>
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
