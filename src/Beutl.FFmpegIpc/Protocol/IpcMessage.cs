using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.FFmpegIpc.Protocol.Messages;

namespace Beutl.FFmpegIpc.Protocol;

public sealed class IpcMessage
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public MessageType Type { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("errorStack")]
    public string? ErrorStackTrace { get; set; }

    public T? GetPayload<T>()
    {
        if (Payload is not { } element)
            return default;
        return JsonSerializer.Deserialize<T>(element, IpcJsonContext.Default.Options);
    }

    public static IpcMessage Create<T>(int id, MessageType type, T payload)
    {
        return new IpcMessage
        {
            Id = id,
            Type = type,
            Payload = JsonSerializer.SerializeToElement(payload, IpcJsonContext.Default.Options),
        };
    }

    public static IpcMessage CreateError(int id, string error, string? stackTrace = null)
    {
        return new IpcMessage
        {
            Id = id,
            Type = MessageType.Error,
            Error = error,
            ErrorStackTrace = stackTrace,
        };
    }

    public static IpcMessage CreateSimple(int id, MessageType type)
    {
        return new IpcMessage { Id = id, Type = type };
    }
}

// Envelope
[JsonSerializable(typeof(IpcMessage))]
// Lifecycle
[JsonSerializable(typeof(HandshakeMessage))]
// Decoding
[JsonSerializable(typeof(OpenFileRequest))]
[JsonSerializable(typeof(OpenFileResponse))]
[JsonSerializable(typeof(ReadVideoRequest))]
[JsonSerializable(typeof(ReadVideoResponse))]
[JsonSerializable(typeof(ReadAudioRequest))]
[JsonSerializable(typeof(ReadAudioResponse))]
[JsonSerializable(typeof(CloseReaderRequest))]
[JsonSerializable(typeof(UpdateDecoderSettingsRequest))]
// Encoding
[JsonSerializable(typeof(EncodeStartRequest))]
[JsonSerializable(typeof(EncodeStartAckMessage))]
[JsonSerializable(typeof(RequestFrameMessage))]
[JsonSerializable(typeof(ProvideFrameMessage))]
[JsonSerializable(typeof(RequestSampleMessage))]
[JsonSerializable(typeof(ProvideSampleMessage))]
[JsonSerializable(typeof(EncodeProgressMessage))]
[JsonSerializable(typeof(EncodeCompleteMessage))]
// Codec queries
[JsonSerializable(typeof(QueryCodecsRequest))]
[JsonSerializable(typeof(QueryCodecsResponse))]
[JsonSerializable(typeof(QueryPixelFormatsRequest))]
[JsonSerializable(typeof(QueryPixelFormatsResponse))]
[JsonSerializable(typeof(QuerySampleRatesRequest))]
[JsonSerializable(typeof(QuerySampleRatesResponse))]
[JsonSerializable(typeof(QueryAudioFormatsRequest))]
[JsonSerializable(typeof(QueryAudioFormatsResponse))]
[JsonSerializable(typeof(QueryDefaultCodecRequest))]
[JsonSerializable(typeof(QueryDefaultCodecResponse))]
// Supporting types
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class IpcJsonContext : JsonSerializerContext;
