using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Protocol.Transport;

/// <summary>
/// Handles serialization and deserialization of operations for transport.
/// </summary>
public static class OperationSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an operation to a JSON string.
    /// </summary>
    /// <param name="operation">The operation to serialize.</param>
    /// <returns>A JSON string representation of the operation.</returns>
    public static string Serialize(OperationBase operation)
    {
        var envelope = new OperationEnvelope
        {
            TypeName = operation.GetType().FullName ?? throw new InvalidOperationException("Operation type has no full name"),
            SequenceNumber = operation.SequenceNumber,
            Payload = JsonSerializer.SerializeToNode(operation, operation.GetType(), s_options) ?? throw new InvalidOperationException("Failed to serialize operation")
        };

        return JsonSerializer.Serialize(envelope, s_options);
    }

    /// <summary>
    /// Deserializes an operation from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized operation.</returns>
    public static OperationBase Deserialize(string json)
    {
        var envelope = JsonSerializer.Deserialize<OperationEnvelope>(json, s_options)
            ?? throw new InvalidOperationException("Failed to deserialize operation envelope");

        var type = Type.GetType(envelope.TypeName)
            ?? throw new InvalidOperationException($"Operation type '{envelope.TypeName}' not found");

        if (!typeof(OperationBase).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Type '{envelope.TypeName}' is not an OperationBase");
        }

        var operation = (OperationBase)(envelope.Payload.Deserialize(type, s_options)
            ?? throw new InvalidOperationException("Failed to deserialize operation payload"));

        operation.SequenceNumber = envelope.SequenceNumber;

        return operation;
    }

    private class OperationEnvelope
    {
        public required string TypeName { get; set; }
        public required long SequenceNumber { get; set; }
        public required JsonNode Payload { get; set; }
    }
}
