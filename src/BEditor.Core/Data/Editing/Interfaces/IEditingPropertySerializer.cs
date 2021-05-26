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
}