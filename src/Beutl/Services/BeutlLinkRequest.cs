using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal abstract record BeutlLinkRequest
{
    // Percent-encoding a 255-character multi-byte UTF-8 ref can exceed 2,048 characters (up to ~3.1 KB).
    internal const int MaxUriLength = 4096;

    public static bool TryParse(string? value, [NotNullWhen(true)] out BeutlLinkRequest? request)
    {
        if (PackageInstallRequest.TryParse(value, out PackageInstallRequest? package))
        {
            request = package;
            return true;
        }

        bool valid = RepositoryOpenRequest.TryParse(value, out RepositoryOpenRequest? repository);
        request = repository;
        return valid;
    }
}

internal sealed record RepositoryOpenRequest(Guid RepositoryId, string? Revision) : BeutlLinkRequest
{
    private static readonly UTF8Encoding s_urlEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool TryParse(string? value, [NotNullWhen(true)] out RepositoryOpenRequest? request)
    {
        request = null;
        const string authority = "beutl://repo";
        const string route = "/open?";
        // The fixed prefix validates the authority and path. Decode the query ourselves so that
        // URI canonicalization cannot reject an escaped UTF-8 ref that also contains reserved punctuation.
        if (value is null || value.Length > MaxUriLength || value.Contains('#')
            || !value.StartsWith(authority, StringComparison.OrdinalIgnoreCase)
            || !value.AsSpan(authority.Length).StartsWith(route, StringComparison.Ordinal))
        {
            return false;
        }

        Guid id = Guid.Empty;
        string? revision = null;
        foreach (string item in value[(authority.Length + route.Length)..].Split('&'))
        {
            int separator = item.IndexOf('=');
            if (separator < 0)
                return false;

            string key = item[..separator];
            if (!TryDecode(item[(separator + 1)..], out string? decoded))
                return false;
            if (key == "id" && id == Guid.Empty)
            {
                if (!Guid.TryParseExact(decoded, "D", out id) || id == Guid.Empty || decoded != id.ToString("D"))
                    return false;
            }
            else if (key == "ref" && revision is null && GitRevisionValidator.IsWebRevision(decoded))
            {
                revision = decoded;
            }
            else
            {
                return false;
            }
        }

        if (id == Guid.Empty)
            return false;

        request = new RepositoryOpenRequest(id, revision);
        return true;
    }

    private static bool TryDecode(string value, [NotNullWhen(true)] out string? decoded)
    {
        decoded = null;
        try
        {
            byte[] bytes = s_urlEncoding.GetBytes(value.Replace('+', ' '));
            int length = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte next = bytes[i];
                if (next == (byte)'%')
                {
                    if (i + 2 >= bytes.Length || !Utf8Parser.TryParse(bytes.AsSpan(i + 1, 2), out next, out int used, 'X') || used != 2)
                        return false;
                    i += 2;
                }
                bytes[length++] = next;
            }
            decoded = s_urlEncoding.GetString(bytes, 0, length);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
