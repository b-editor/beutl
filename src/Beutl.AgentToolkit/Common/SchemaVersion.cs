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

        document.TryGetPropertyValue(PropertyName, out JsonNode? node);
        if (node is null)
        {
            EnsureKnown((string?)null);
            return;
        }

        if (node is JsonValue value && value.TryGetValue(out string? version))
        {
            EnsureKnown(version);
            return;
        }

        // An agent can write the version as a number, such as 1, which is a wrong version too.
        string text = node.ToJsonString();
        throw new SchemaVersionMismatchException(
            text,
            $"The agent toolkit schema version must be the string \"{Current}\", not {text}.");
    }
}

public sealed class SchemaVersionMismatchException : Exception
{
    public SchemaVersionMismatchException(string? version)
        : this(version, $"Unsupported agent toolkit schema version '{version ?? "<missing>"}'.")
    {
    }

    internal SchemaVersionMismatchException(string? version, string message)
        : base(message)
    {
        Version = version;
    }

    public string Code => ErrorCode.SchemaVersionMismatch;

    public string? Version { get; }
}
