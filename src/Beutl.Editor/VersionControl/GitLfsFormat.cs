using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Beutl.Editor.VersionControl;

// The text formats of Git LFS: pointer files and the object listings of git lfs ls-files.
internal static class GitLfsFormat
{
    internal const int MaxPointerBytes = 1024;

    internal static bool IsLfsPointer(byte[] contents)
    {
        if (contents.Length == 0 || contents.Length > MaxPointerBytes)
        {
            return false;
        }

        // Git LFS's non-strict decoder operates on bytes and can accept a malformed UTF-8
        // extension name. Replacement decoding retains the ASCII core/extension prefix, so this
        // safety check does not miss a pointer that the unavailable smudge filter would consume.
        string pointer = Encoding.UTF8.GetString(contents).Trim();

        string[] lines = pointer.Split('\n');
        int lineCount = lines.Length;
        if (lineCount > 0 && lines[^1].Length == 0)
        {
            lineCount--;
        }

        for (int i = 0; i < lineCount; i++)
        {
            if (lines[i].EndsWith('\r'))
            {
                lines[i] = lines[i][..^1];
            }
        }

        int index = 0;
        var extensionPriorities = new Dictionary<int, string>();
        if (!SkipLfsPointerExtensions(
                lines,
                lineCount,
                ref index,
                extensionPriorities))
        {
            return false;
        }

        if (index >= lineCount
            || !IsSupportedLfsPointerVersion(lines[index]))
        {
            return false;
        }

        index++;
        if (!SkipLfsPointerExtensions(
                lines,
                lineCount,
                ref index,
                extensionPriorities))
        {
            return false;
        }

        const string OidPrefix = "oid sha256:";
        if (index >= lineCount
            || !lines[index].StartsWith(OidPrefix, StringComparison.Ordinal)
            || lines[index].Length != OidPrefix.Length + 64
            || !IsCanonicalLfsOid(lines[index].AsSpan(OidPrefix.Length)))
        {
            return false;
        }

        index++;
        if (!SkipLfsPointerExtensions(
                lines,
                lineCount,
                ref index,
                extensionPriorities))
        {
            return false;
        }

        const string SizePrefix = "size ";
        if (index >= lineCount
            || !lines[index].StartsWith(SizePrefix, StringComparison.Ordinal)
            || !IsNonNegativeLfsSize(lines[index].AsSpan(SizePrefix.Length)))
        {
            return false;
        }

        index++;
        while (index < lineCount && lines[index].Length == 0)
        {
            index++;
        }

        return index == lineCount;
    }

    private static bool IsSupportedLfsPointerVersion(string line)
    {
        return line is "version https://git-lfs.github.com/spec/v1"
            or "version http://git-media.io/v/2"
            or "version https://hawser.github.com/spec/v1";
    }

    private static bool SkipLfsPointerExtensions(
        string[] lines,
        int lineCount,
        ref int index,
        Dictionary<int, string> priorities)
    {
        while (index < lineCount)
        {
            string line = lines[index];
            if (line.Length == 0)
            {
                index++;
                continue;
            }

            if (!TryParseLfsPointerExtension(line, out int priority, out string key))
            {
                break;
            }

            if (priorities.TryGetValue(priority, out string? existingKey)
                && !string.Equals(existingKey, key, StringComparison.Ordinal))
            {
                return false;
            }

            priorities[priority] = key;
            index++;
        }

        return true;
    }

    private static bool TryParseLfsPointerExtension(
        string line,
        out int priority,
        out string key)
    {
        priority = 0;
        key = string.Empty;
        int separator = line.IndexOf(' ');
        if (separator < 7 || separator >= line.Length - 1)
        {
            return false;
        }

        key = line[..separator];
        if (!key.StartsWith("ext-", StringComparison.Ordinal)
            || key[4] is not (>= '0' and <= '9')
            || key[5] != '-'
            || !IsAsciiWordCharacter(key[6]))
        {
            return false;
        }

        const string OidPrefix = "sha256:";
        ReadOnlySpan<char> value = line.AsSpan(separator + 1);
        if (!value.StartsWith(OidPrefix, StringComparison.Ordinal)
            || value.Length != OidPrefix.Length + 64
            || !IsCanonicalLfsOid(value[OidPrefix.Length..]))
        {
            return false;
        }

        priority = key[4] - '0';
        return true;
    }

    private static bool IsAsciiWordCharacter(char value)
    {
        return value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_';
    }

    private static bool IsNonNegativeLfsSize(ReadOnlySpan<char> value)
    {
        return long.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out long size)
               && size >= 0;
    }

    internal static bool TryParseCanonicalLfsObjectList(
        string json,
        out IReadOnlyList<string> oids)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("files", out JsonElement files))
            {
                oids = [];
                return false;
            }

            if (files.ValueKind == JsonValueKind.Null)
            {
                oids = [];
                return true;
            }

            if (files.ValueKind != JsonValueKind.Array)
            {
                oids = [];
                return false;
            }

            var parsed = new List<string>(files.GetArrayLength());
            foreach (JsonElement file in files.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object
                    || !file.TryGetProperty("oid", out JsonElement oidElement)
                    || oidElement.ValueKind != JsonValueKind.String
                    || oidElement.GetString() is not { } oid
                    || oid.Length != 64
                    || !IsCanonicalLfsOid(oid))
                {
                    oids = [];
                    return false;
                }

                parsed.Add(oid);
            }

            oids = parsed;
            return true;
        }
        catch (JsonException)
        {
            oids = [];
            return false;
        }
    }

    internal static bool TryParseCanonicalLfsObjectLines(
        string output,
        out IReadOnlyList<string> oids)
    {
        if (output.Length == 0)
        {
            oids = [];
            return true;
        }

        var parsed = new List<string>();
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            const int OidLength = 64;
            if (line.Length >= OidLength + 3
                && line[OidLength] == ' '
                && line[OidLength + 1] is '*' or '-'
                && line[OidLength + 2] == ' '
                && IsCanonicalLfsOid(line.AsSpan(0, OidLength)))
            {
                parsed.Add(line[..OidLength]);
            }
            else if (parsed.Count == 0 && line.Length != 0)
            {
                oids = [];
                return false;
            }
        }

        oids = parsed;
        return parsed.Count > 0;
    }

    private static bool IsCanonicalLfsOid(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
