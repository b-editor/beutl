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

        // Looser than Git LFS's own decoder, so no pointer that the unavailable smudge filter would
        // consume is missed. Text that only looks like a pointer makes the caller ask for Git LFS.
        string[] lines = Encoding.UTF8.GetString(contents).Trim().Split('\n');
        return lines.Any(static line => IsSupportedLfsPointerVersion(line.TrimEnd('\r')))
               && lines.Any(static line => line.StartsWith("oid sha256:", StringComparison.Ordinal))
               && lines.Any(static line => line.StartsWith("size ", StringComparison.Ordinal));
    }

    private static bool IsSupportedLfsPointerVersion(string line)
    {
        return line is "version https://git-lfs.github.com/spec/v1"
            or "version http://git-media.io/v/2"
            or "version https://hawser.github.com/spec/v1";
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
