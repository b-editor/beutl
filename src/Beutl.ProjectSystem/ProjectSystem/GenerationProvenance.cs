using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.ProjectSystem;

/// <summary>
/// Self-asserted, producer-neutral metadata describing how an element was generated.
/// The payload is intentionally opaque so newer producers can round-trip data through
/// older Beutl versions without the editor interpreting or discarding it.
/// Payloads are persisted in project files and must not contain credentials, private prompts,
/// account identifiers, or identifiers of remote jobs and files.
/// </summary>
public sealed record GenerationProvenance
{
    private const int MaxPayloadBytes = 32 * 1024;

    [JsonConstructor]
    public GenerationProvenance(
        string producerId,
        string operation,
        int schemaVersion,
        JsonElement payload,
        DateTimeOffset generatedAt)
    {
        ProducerId = NormalizeName(producerId, nameof(producerId));
        Operation = NormalizeName(operation, nameof(operation));
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The provenance payload must be a JSON object.", nameof(payload));
        if (generatedAt == default)
            throw new ArgumentException("The generation timestamp must be specified.", nameof(generatedAt));

        string payloadJson = payload.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadBytes)
            throw new ArgumentException("The provenance payload is too large.", nameof(payload));

        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
        GeneratedAt = generatedAt.ToUniversalTime();
    }

    [JsonPropertyName("producerId")]
    public string ProducerId { get; }

    [JsonPropertyName("operation")]
    public string Operation { get; }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; }

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; }

    public bool Equals(GenerationProvenance? other)
        => other is not null
            && string.Equals(ProducerId, other.ProducerId, StringComparison.Ordinal)
            && string.Equals(Operation, other.Operation, StringComparison.Ordinal)
            && SchemaVersion == other.SchemaVersion
            && JsonElement.DeepEquals(Payload, other.Payload)
            && GeneratedAt.Equals(other.GeneratedAt);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProducerId, StringComparer.Ordinal);
        hash.Add(Operation, StringComparer.Ordinal);
        hash.Add(SchemaVersion);
        AddPayloadHash(ref hash, Payload);
        hash.Add(GeneratedAt);
        return hash.ToHashCode();
    }

    public static bool TryCreate(
        string producerId,
        string operation,
        int schemaVersion,
        JsonElement payload,
        DateTimeOffset generatedAt,
        out GenerationProvenance? provenance)
    {
        try
        {
            provenance = new GenerationProvenance(
                producerId,
                operation,
                schemaVersion,
                payload,
                generatedAt);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            provenance = null;
            return false;
        }
    }

    private static string NormalizeName(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        string normalized = value.Trim();
        if (normalized.Length is 0 or > 128
            || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            throw new ArgumentException(
                "The value must contain only ASCII letters, digits, '.', '_' or '-'.",
                parameterName);
        }

        return normalized;
    }

    private static void AddPayloadHash(ref HashCode hash, JsonElement element)
    {
        hash.Add(element.ValueKind);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    hash.Add(property.Name, StringComparer.Ordinal);
                    AddPayloadHash(ref hash, property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AddPayloadHash(ref hash, item);
                }
                break;
            case JsonValueKind.String:
                hash.Add(element.GetString(), StringComparer.Ordinal);
                break;
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out decimal decimalValue))
                    hash.Add(decimalValue);
                else if (element.TryGetDouble(out double doubleValue))
                    hash.Add(doubleValue);
                else
                    hash.Add(element.GetRawText(), StringComparer.Ordinal);
                break;
            case JsonValueKind.True:
                hash.Add(true);
                break;
            case JsonValueKind.False:
                hash.Add(false);
                break;
        }
    }
}
