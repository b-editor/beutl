using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

/// <summary>
/// Reads an error code the server names in camelCase, and reads one this
/// client has never heard of as <see cref="ApiErrorCode.Unknown"/>.
/// </summary>
/// <remarks>
/// A server is free to add codes; refusing to parse one would throw away the
/// whole error body, turning a failure the client could still report — and its
/// status, and its message — into an unexplained one. Unknown is what the enum
/// already means by "a code with no handling of its own".
/// </remarks>
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
