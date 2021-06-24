using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Data;

namespace BEditor.Models
{
    public class EffectWrapper : IJsonObject
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

        public void SetObjectData(JsonElement element)
        {
            var typeName = element.GetProperty("_type").GetString() ?? string.Empty;
            if (Type.GetType(typeName) is var type && type is not null)
            {
                Effect = (EffectElement)FormatterServices.GetUninitializedObject(type);
                Effect.SetObjectData(element);
            }
        }
    }
}