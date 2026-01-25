using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.JsonConverters;

public class ReferenceJsonConverter : JsonConverter<IReference>
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (typeToConvert.IsGenericType
            && typeToConvert.GetGenericTypeDefinition() == typeof(Reference<>))
        {
            Type objectType = typeToConvert.GetGenericArguments()[0];
            return typeof(CoreObject).IsAssignableFrom(objectType);
        }

        return false;
    }

    public override IReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Guid id = reader.GetGuid();
        return (IReference?)Activator.CreateInstance(typeToConvert, id);
    }

    public override void Write(Utf8JsonWriter writer, IReference value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Id);
    }
}
