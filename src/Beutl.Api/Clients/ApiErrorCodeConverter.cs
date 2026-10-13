using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

/// <summary>
/// Reads a camelCase error code, and an unknown one as <see cref="ApiErrorCode.Unknown"/>, so that an unknown
/// code does not throw away the whole error body.
/// </summary>
internal sealed class ApiErrorCodeConverter : JsonConverter<ApiErrorCode>
{
    public override ApiErrorCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && reader.GetString() is { Length: > 0 } name
            && !char.IsAsciiDigit(name[0])
            && Enum.TryParse(name, ignoreCase: true, out ApiErrorCode parsed))
        {
            return parsed;
        }

        if (reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out int numeric)
            && Enum.IsDefined((ApiErrorCode)numeric))
        {
            return (ApiErrorCode)numeric;
        }

        return ApiErrorCode.Unknown;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ApiErrorCode value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
