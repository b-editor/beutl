using System.Text.Json;

namespace BEditor.Data
{
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
}