using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Editor.Services;

/// <summary>
/// Parsing helpers for clipboard payloads. <see cref="JsonNode.Parse(string, System.Text.Json.Nodes.JsonNodeOptions?, JsonDocumentOptions)"/>
/// throws <see cref="JsonException"/> on malformed input, but clipboard text is
/// untrusted, so this returns <see langword="null"/> on failure to let callers
/// map it onto their own "invalid JSON" outcome instead of propagating the
/// exception out of a paste command.
/// </summary>
internal static class ClipboardJson
{
    public static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
