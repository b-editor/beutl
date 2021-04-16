using System.Runtime.Serialization;
using System.Text.Json;

namespace BEditor.Data
{
    internal class PropertyJsonSerializer<TValue> : IEditingPropertySerializer<TValue> where TValue : IJsonObject
    {
        public static readonly PropertyJsonSerializer<TValue> Current = new();

        public TValue Read(JsonElement element)
        {
            var value = (TValue)FormatterServices.GetUninitializedObject(typeof(TValue));
            value.SetObjectData(element);

            return value;
        }

        public void Write(Utf8JsonWriter writer, TValue value)
        {
            value.GetObjectData(writer);
        }
    }
}