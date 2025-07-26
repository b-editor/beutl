using System.Text.Json.Serialization;

namespace Beutl.Synchronization.Core;

/// <summary>
/// Represents a property change notification that can be synchronized across clients
/// </summary>
public record ChangeNotification
{
    /// <summary>
    /// Unique identifier of the CoreObject that changed
    /// </summary>
    [JsonPropertyName("objectId")]
    public required Guid ObjectId { get; init; }

    /// <summary>
    /// Name of the property that changed
    /// </summary>
    [JsonPropertyName("propertyName")]
    public required string PropertyName { get; init; }

    /// <summary>
    /// New value of the property (JSON serialized)
    /// </summary>
    [JsonPropertyName("newValue")]
    public object? NewValue { get; init; }

    /// <summary>
    /// Previous value of the property (JSON serialized)
    /// </summary>
    [JsonPropertyName("oldValue")]
    public object? OldValue { get; init; }

    /// <summary>
    /// UTC timestamp when the change occurred
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Source of the change ("client", "server", or specific client ID)
    /// </summary>
    [JsonPropertyName("changeSource")]
    public required string ChangeSource { get; init; }

    /// <summary>
    /// Session ID for grouping related changes
    /// </summary>
    [JsonPropertyName("sessionId")]
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Optional sequence number for ordering changes
    /// </summary>
    [JsonPropertyName("sequenceNumber")]
    public long? SequenceNumber { get; init; }

    /// <summary>
    /// Type name of the CoreObject for deserialization context
    /// </summary>
    [JsonPropertyName("objectTypeName")]
    public string? ObjectTypeName { get; init; }
}

/// <summary>
/// Enumeration of change notification sources
/// </summary>
public static class ChangeSource
{
    public const string Client = "client";
    public const string Server = "server";
    public const string LocalClient = "local_client";
    public const string RemoteClient = "remote_client";
}