using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BEditor.Data
{
#pragma warning disable CS1591
    public interface IJsonObject
    {
        public void GetObjectData(Utf8JsonWriter writer);
        public void SetObjectData(JsonElement element);
    }
#pragma warning restore CS1591
}
