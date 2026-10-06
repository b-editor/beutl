using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Beutl.ProjectSystem;

// Reads what an element file that failed to deserialize still says about itself: its top-level Id
// and type name, from the parsed tree when the text is JSON and from a raw scan when it is not.
internal static class RecoveredElementJsonScanner
{
    private static readonly Regex s_idPattern = new(
        "\"Id\"\\s*:\\s*\"(?<id>[0-9a-fA-F-]{36})\"",
        RegexOptions.CultureInvariant);
    private static readonly Regex s_typePattern = new(
        "\"\\$type\"\\s*:\\s*(?<type>\"(?:\\\\.|[^\"\\\\])*\")",
        RegexOptions.CultureInvariant);
    private static readonly Regex s_legacyTypePattern = new(
        "\"@type\"\\s*:\\s*(?<type>\"(?:\\\\.|[^\"\\\\])*\")",
        RegexOptions.CultureInvariant);

    public static bool TryGetSerializedId(JsonObject? json, out Guid id)
    {
        id = Guid.Empty;
        return json is not null
               && json.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
               && idNode is JsonValue idValue
               && idValue.TryGetValue(out string? idText)
               && Guid.TryParse(idText, out id)
               && id != Guid.Empty;
    }

    public static JsonObject? TryParseTopLevelObject(string rawText)
    {
        try
        {
            return JsonNode.Parse(rawText) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string DecodeRecoveryMetadata(byte[] rawBytes)
    {
        ReadOnlySpan<byte> bytes = rawBytes;
        if (bytes.Length >= 4
            && bytes[0] == 0xff && bytes[1] == 0xfe
            && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return Encoding.UTF32.GetString(bytes[4..]);
        }

        if (bytes.Length >= 4
            && bytes[0] == 0x00 && bytes[1] == 0x00
            && bytes[2] == 0xfe && bytes[3] == 0xff)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetString(bytes[4..]);
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public static string? TryGetTopLevelTypeName(
        ReadOnlySpan<byte> rawBytes,
        string rawText,
        JsonObject? root)
    {
        if (root?.TryGetDiscriminator(out string? parsedTypeName) == true)
        {
            return parsedTypeName;
        }

        if (TryGetTopLevelStringProperty(rawBytes, "$type", out string? scannedTypeName))
        {
            return scannedTypeName;
        }

        if (TryGetTopLevelStringProperty(rawBytes, "@type", out string? scannedLegacyTypeName))
        {
            return scannedLegacyTypeName;
        }

        Match? match = FindTopLevelMatch(rawText, s_typePattern.Matches(rawText))
                       ?? FindTopLevelMatch(rawText, s_legacyTypePattern.Matches(rawText));
        if (match is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(match.Groups["type"].Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool TryGetTopLevelId(
        ReadOnlySpan<byte> rawBytes,
        string rawText,
        JsonObject? root,
        out Guid id)
    {
        if (TryGetSerializedId(root, out Guid parsedId))
        {
            id = parsedId;
            return true;
        }

        if (TryGetTopLevelStringProperty(rawBytes, nameof(CoreObject.Id), out string? scannedId)
            && Guid.TryParse(scannedId, out Guid scannedGuid)
            && scannedGuid != Guid.Empty)
        {
            id = scannedGuid;
            return true;
        }

        // Only a top-level Id may name the element: a nested object's or quoted Id would collide
        // with live objects, so anything else falls through to the deterministic filename Guid.
        MatchCollection matches = s_idPattern.Matches(rawText);
        Match? topLevelMatch = FindTopLevelMatch(rawText, matches);
        if (topLevelMatch != null
            && Guid.TryParse(topLevelMatch.Groups["id"].Value, out Guid topLevelId)
            && topLevelId != Guid.Empty)
        {
            id = topLevelId;
            return true;
        }

        id = Guid.Empty;
        return false;
    }

    private static bool TryGetTopLevelStringProperty(
        ReadOnlySpan<byte> rawBytes,
        string propertyName,
        out string? value)
    {
        value = null;
        if (rawBytes.Length >= 3
            && rawBytes[0] == 0xef
            && rawBytes[1] == 0xbb
            && rawBytes[2] == 0xbf)
        {
            rawBytes = rawBytes[3..];
        }

        var reader = new Utf8JsonReader(rawBytes, isFinalBlock: false, state: default);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            int propertyDepth = reader.CurrentDepth + 1;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.CurrentDepth == propertyDepth
                    && reader.ValueTextEquals(propertyName))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                    {
                        value = reader.GetString();
                        return value is not null;
                    }

                    return false;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static Match? FindTopLevelMatch(string rawText, MatchCollection matches)
    {
        int rootStart = 0;
        while (rootStart < rawText.Length
               && (char.IsWhiteSpace(rawText[rootStart]) || rawText[rootStart] == '\uFEFF'))
        {
            rootStart++;
        }

        if (rootStart >= rawText.Length || rawText[rootStart] != '{')
        {
            return null;
        }

        int matchIndex = 0;
        int objectDepth = 0;
        int arrayDepth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = rootStart; i < rawText.Length && matchIndex < matches.Count; i++)
        {
            Match match = matches[matchIndex];
            if (i == match.Index)
            {
                if (!inString && objectDepth == 1 && arrayDepth == 0)
                {
                    return match;
                }

                matchIndex++;
            }

            char current = rawText[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
            }
            else if (current == '"')
            {
                inString = true;
            }
            else if (current == '{')
            {
                objectDepth++;
            }
            else if (current == '}' && objectDepth > 0)
            {
                objectDepth--;
                if (objectDepth == 0)
                {
                    return null;
                }
            }
            else if (current == '[')
            {
                arrayDepth++;
            }
            else if (current == ']' && arrayDepth > 0)
            {
                arrayDepth--;
            }
        }

        return null;
    }
}
