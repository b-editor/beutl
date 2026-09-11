namespace Beutl.Serialization;

internal static class UriHelper
{
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

    public static Uri CreateFromPath(string path)
    {
        return new Uri(path);
    }
}
