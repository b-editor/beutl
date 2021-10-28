using System;
using System.Runtime.Serialization;
using System.Text.Json;

using BEditor.Data;

namespace BEditor.Models
{
    public sealed class EffectWrapper : IJsonObject
    {
        public EffectWrapper(EffectElement effect)
        {
            Effect = effect;
        }

        public EffectElement Effect { get; set; }

        public void GetObjectData(Utf8JsonWriter writer)
        {
            var type = Effect.GetType();
            writer.WriteString("_type", type.FullName + ", " + type.Assembly.GetName().Name);
            Effect.GetObjectData(writer);
        }

        public void SetObjectData(DeserializeContext context)
        {
            var element = context.Element;
            var typeName = element.GetProperty("_type").GetString() ?? string.Empty;
            if (Type.GetType(typeName) is var type && type is not null)
            {
                Effect = (EffectElement)FormatterServices.GetUninitializedObject(type);
                Effect.SetObjectData(context);
            }
        }
    }
}