using System.Globalization;
using System.Text;

namespace Beutl.Serialization;

internal static class UriHelper
{
    // What new Uri(path) leaves unescaped in a file path, besides ASCII letters and digits.
    private const string UnescapedPathCharacters = "-._~!$&'()*+,;=:@[]";

    // How a reference to uri is written: relative to baseUri when both share a scheme.
    public static Uri ToSerializedUri(Uri uri, Uri? baseUri)
    {
        if (baseUri?.Scheme == uri.Scheme)
        {
            return baseUri.MakeRelativeUri(uri);
        }

        return uri;
    }

    // Uri.ToString() unescapes "%25" in an absolute URI, so a name with "%XX" would read back as another file.
    public static string ToPersistedReference(Uri serializedUri)
        => serializedUri.IsAbsoluteUri ? serializedUri.AbsoluteUri : serializedUri.ToString();

    public static Uri ResolvePersistedReference(string? value, Uri? baseUri, bool allowRelative = false)
    {
        if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri? uri))
            throw new System.Text.Json.JsonException($"Invalid URI: {value}");
        if (uri.IsAbsoluteUri)
            return uri;
        if (baseUri is null)
        {
            if (allowRelative) return uri;
            throw new System.Text.Json.JsonException("Cannot resolve relative URI without a base URI.");
        }

        // Native file-path Uris treat relative strings as paths, escaping '%' again.
        // Persisted references are already URI-escaped: resolve them against an explicit
        // URI so literal percent sequences and fragment characters are decoded only once.
        if (baseUri.IsAbsoluteUri)
            baseUri = new Uri(baseUri.AbsoluteUri, UriKind.Absolute);
        if (!Uri.TryCreate(baseUri, uri, out Uri? resolved))
            throw new System.Text.Json.JsonException($"Invalid relative URI: {value}");
        return resolved;
    }

    public static byte[] ResolveByteArray(Uri uri)
    {
        if (uri.Scheme == "data")
        {
            var (data, _) = ParseDataUri(uri);
            return data;
        }
        else if (uri.Scheme == "file")
        {
            return File.ReadAllBytes(uri.LocalPath);
        }
        else
        {
            throw new NotSupportedException($"URI scheme '{uri.Scheme}' is not supported.");
        }
    }

    public static Stream ResolveStream(Uri uri)
    {
        if (uri.Scheme == "data")
        {
            var (data, _) = ParseDataUri(uri);
            return new MemoryStream(data);
        }
        else if (uri.Scheme == "file")
        {
            return File.OpenRead(uri.LocalPath);
        }
        else
        {
            throw new NotSupportedException($"URI scheme '{uri.Scheme}' is not supported.");
        }
    }

    public static (byte[] Data, string Metadata) ParseDataUri(Uri uri)
    {
        // data:[<mediatype>][;base64],<data>
        string uriString = uri.OriginalString;
        int commaIndex = uriString.IndexOf(',');
        if (commaIndex < 0)
        {
            throw new FormatException("Invalid data URI format.");
        }

        string metadata = uriString[5..commaIndex]; // "data:"の後ろからカンマまで
        string dataPart = uriString[(commaIndex + 1)..];

        bool isBase64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        if (isBase64)
        {
            return (Convert.FromBase64String(dataPart), metadata);
        }
        else
        {
            return (System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(dataPart)), metadata);
        }
    }

    public static Uri CreateBase64DataUri(string mediaType, byte[] data)
    {
        string base64Data = Convert.ToBase64String(data);
        string uriString = $"data:{mediaType};base64,{base64Data}";
        return new Uri(uriString);
    }

    // Escapes as new Uri(path) does, and '%' too, so that LocalPath is the path even when a name holds "%XX".
    public static Uri CreateFromPath(string path) => CreateFromPath(path, OperatingSystem.IsWindows());

    // windows: the path has a drive, which stays, or a UNC server, which becomes the host. Other forms go to new Uri(path).
    internal static Uri CreateFromPath(string path, bool windows)
    {
        var builder = new StringBuilder("file://");
        int start = 0;
        if (windows && path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2], windows))
        {
            builder.Append('/');
        }
        else if (windows && path.Length >= 3 && IsSeparator(path[0], windows) && IsSeparator(path[1], windows)
                 && !IsSeparator(path[2], windows) && path[2] is not ('?' or '.'))
        {
            int end = path.AsSpan(2).IndexOfAny('\\', '/');
            start = end < 0 ? path.Length : end + 2;
            builder.Append(path, 2, start - 2);
        }
        else if (windows || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
        {
            return new Uri(path);
        }

        Span<byte> utf8 = stackalloc byte[4];
        foreach (Rune rune in path.AsSpan(start).EnumerateRunes())
        {
            if (rune.IsAscii && IsSeparator((char)rune.Value, windows))
            {
                builder.Append('/');
            }
            else if (rune.IsAscii
                     && (char.IsAsciiLetterOrDigit((char)rune.Value) || UnescapedPathCharacters.Contains((char)rune.Value)))
            {
                builder.Append((char)rune.Value);
            }
            else
            {
                foreach (byte b in utf8[..rune.EncodeToUtf8(utf8)])
                {
                    builder.Append(CultureInfo.InvariantCulture, $"%{b:X2}");
                }
            }
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static bool IsSeparator(char c, bool windows) => c == '/' || (windows && c == '\\');
}
