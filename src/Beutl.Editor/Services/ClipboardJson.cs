using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Editor.Services;

/// <summary>
/// Parsing helpers for clipboard payloads. Returns <see langword="null"/> on
/// malformed input (clipboard text is untrusted) so callers map it to their own
/// "invalid JSON" outcome instead of an exception escaping the paste command.
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
