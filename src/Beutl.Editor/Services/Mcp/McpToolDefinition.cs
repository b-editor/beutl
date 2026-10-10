using System.Text.Json;

namespace Beutl.Editor.Services.Mcp;

/// <summary>Describes one MCP tool contributed by a <see cref="McpToolExtension"/>.</summary>
public sealed class McpToolDefinition
{
    private const int MaxNameLength = 128;

    private static readonly JsonElement s_emptyObjectSchema =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    /// <param name="name">
    /// The tool name the agent calls: 1 to 128 ASCII letters, digits, <c>_</c>, <c>-</c>, or <c>.</c>.
    /// </param>
    /// <param name="description">Tells the agent what the tool does and when to call it.</param>
    /// <param name="inputSchema">
    /// A JSON Schema object with <c>"type": "object"</c> that describes the arguments. Omit it for a
    /// tool without arguments. Arguments that are neither listed under <c>properties</c> nor matched by
    /// a <c>patternProperties</c> pattern are rejected before the tool runs, unless the schema sets
    /// <c>additionalProperties</c> to something other than <see langword="false"/> or combines
    /// subschemas at the top level (<c>allOf</c>, <c>anyOf</c>, <c>oneOf</c>, <c>if</c>,
    /// <c>dependentSchemas</c>, or <c>$ref</c>). A tool that declares the reserved <c>instanceId</c>
    /// property is skipped.
    /// </param>
    public McpToolDefinition(string name, string description, JsonElement? inputSchema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!IsValidName(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid MCP tool name. Use 1 to {MaxNameLength} ASCII letters, digits, '_', '-', or '.'.",
                nameof(name));
        }

        JsonElement schema = inputSchema ?? s_emptyObjectSchema;
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("type", out JsonElement type)
            || type.ValueKind != JsonValueKind.String
            || !type.ValueEquals("object"))
        {
            throw new ArgumentException(
                "An MCP tool input schema must be a JSON object with \"type\": \"object\".",
                nameof(inputSchema));
        }

        Name = name;
        Description = description;
        InputSchema = schema.Clone();
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement InputSchema { get; }

    /// <summary>A human-readable name that clients may show instead of <see cref="Name"/>.</summary>
    public string? Title { get; init; }

    /// <summary>Whether the tool leaves the project, files, and other state unchanged.</summary>
    public bool? ReadOnlyHint { get; init; }

    /// <summary>Whether the tool may delete or overwrite existing data.</summary>
    public bool? DestructiveHint { get; init; }

    /// <summary>Whether repeating a call with the same arguments has no further effect.</summary>
    public bool? IdempotentHint { get; init; }

    /// <summary>Whether the tool reaches services or data outside Beutl, such as the network.</summary>
    public bool? OpenWorldHint { get; init; }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return false;

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
                return false;
        }

        return true;
    }
}
