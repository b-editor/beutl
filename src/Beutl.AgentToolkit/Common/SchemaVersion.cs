using System.Text.Json.Nodes;

namespace Beutl.AgentToolkit.Common;

public static class SchemaVersion
{
    public const string Current = "1";
    public const string PropertyName = "schemaVersion";

    public static void Stamp(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document[PropertyName] = Current;
    }

    public static void EnsureKnown(string? version)
    {
        if (version != Current)
        {
            throw new SchemaVersionMismatchException(version);
        }
    }

    public static void EnsureKnown(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string? version = document.TryGetPropertyValue(PropertyName, out JsonNode? node)
            ? node?.GetValue<string>()
            : null;
        EnsureKnown(version);
    }
}

public sealed class SchemaVersionMismatchException : Exception
{
    public SchemaVersionMismatchException(string? version)
        : base($"Unsupported agent toolkit schema version '{version ?? "<missing>"}'.")
    {
        Version = version;
    }

    public string Code => ErrorCode.SchemaVersionMismatch;

    public string? Version { get; }
}
