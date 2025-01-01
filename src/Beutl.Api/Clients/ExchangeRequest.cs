using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class ExchangeRequest
{
    [JsonPropertyName("code")]
    public required  string Code { get; init; }

    [JsonPropertyName("session_id")]
    public required  string SessionId { get; init; }
}
