using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Api.Services;

/// <summary>
/// How the AI endpoints' JSON is read outside Refit. The same shape Refit is
/// configured with, so a body reads the same whichever path it came in on.
/// </summary>
internal static class AiStreamJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// The JSON.stringify-compatible representation used for request bodies
    /// whose byte budget is measured before transport.
    /// </summary>
    public static JsonSerializerOptions CanonicalOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
