using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal class JsonHelper
{
    public static bool TryGetEnum<TEnum>(JsonObject? jobj, string key, out TEnum result)
        where TEnum : struct
    {
        result = default;
        return jobj?.TryGetPropertyValue(key, out JsonNode? node) == true
            && node is JsonValue value
            && value.TryGetValue(out string? str)
            && Enum.TryParse(str, out result);
    }

    public static bool TryGetInt(JsonObject? jobj, string key, out int result)
    {
        result = default;
        return jobj?.TryGetPropertyValue(key, out JsonNode? node) == true
            && node is JsonValue value
            && value.TryGetValue(out result);
    }

    public static bool TryGetString(JsonObject? jobj, string key, [NotNullWhen(true)] out string? result)
    {
        result = default;
        return jobj?.TryGetPropertyValue(key, out JsonNode? node) == true
            && node is JsonValue value
            && value.TryGetValue(out result);
    }
}
