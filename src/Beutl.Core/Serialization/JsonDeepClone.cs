using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Beutl.Utilities;

namespace Beutl.Serialization;

internal static class JsonDeepClone
{
    public static void CopyTo(JsonObject source, JsonObject destination)
    {
        foreach (KeyValuePair<string, JsonNode?> item in source)
        {
            if (item.Value == null)
            {
                destination[item.Key] = null;
            }
            else
            {
                using var bufferWriter = new PooledArrayBufferWriter<byte>();
                using var writer = new Utf8JsonWriter(bufferWriter);
                item.Value.WriteTo(writer);

                writer.Flush();

                destination[item.Key] = JsonNode.Parse(bufferWriter.WrittenSpan);
            }
        }
    }
}
